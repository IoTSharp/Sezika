using ILGPU;
using ILGPU.Algorithms;

namespace Sezika.Kernels;

// Build-time C# kernels. Shapes, work sizes and total runtime are admitted by the
// static CUDA Driver caller; no runtime ILGPU launcher is used.
public static class EncoderBackwardKernels
{
    public static void Clear(ArrayView<float> data, int count)
    { int i=Grid.GlobalIndex.X; if(i<count) data[i]=0; }

    public static void Copy(ArrayView<float> source, ArrayView<float> destination, int count)
    { int i=Grid.GlobalIndex.X; if(i<count) destination[i]=source[i]; }

    public static void LinearInput(ArrayView<float> dy, ArrayView<float> weight, ArrayView<float> dx, int rows, int input, int output)
    {
        int i=Grid.GlobalIndex.X; if(i>=rows*input) return;
        int row=i/input, feature=i%input; float sum=0;
        for(int o=0;o<output;o++) sum+=dy[row*output+o]*weight[o*input+feature];
        dx[i]=sum;
    }
    public static void LinearWeight(ArrayView<float> x, ArrayView<float> dy, ArrayView<float> gradient, int rows, int input, int output)
    {
        int i=Grid.GlobalIndex.X; if(i>=input*output) return;
        int o=i/input, feature=i%input; float sum=0;
        for(int row=0;row<rows;row++) sum+=dy[row*output+o]*x[row*input+feature];
        gradient[i]+=sum;
    }
    public static void NormInput(ArrayView<float> x, ArrayView<float> gamma, ArrayView<float> dy,
        ArrayView<float> dx, ArrayView<float> xhat, int rows, int hidden, float epsilon)
    {
        int row=Grid.GlobalIndex.X; if(row>=rows) return;
        int start=row*hidden; float mean=0, variance=0;
        for(int d=0;d<hidden;d++) mean+=x[start+d]; mean/=hidden;
        for(int d=0;d<hidden;d++) { float v=x[start+d]-mean; variance+=v*v; }
        float inverse=1f/XMath.Sqrt(variance/hidden+epsilon), sum=0, dot=0;
        for(int d=0;d<hidden;d++) { float z=(x[start+d]-mean)*inverse; xhat[start+d]=z; float g=dy[start+d]*gamma[d]; sum+=g; dot+=g*z; }
        for(int d=0;d<hidden;d++) dx[start+d]=inverse*(dy[start+d]*gamma[d]-sum/hidden-xhat[start+d]*dot/hidden);
    }
    public static void NormWeight(ArrayView<float> xhat, ArrayView<float> dy, ArrayView<float> gradient, int rows, int hidden)
    {
        int d=Grid.GlobalIndex.X; if(d>=hidden) return; float sum=0;
        for(int row=0;row<rows;row++) sum+=xhat[row*hidden+d]*dy[row*hidden+d];
        gradient[d]+=sum;
    }
    public static void GatedGelu(ArrayView<float> up, ArrayView<float> dy, ArrayView<float> dup, int rows, int intermediate)
    {
        int i=Grid.GlobalIndex.X; if(i>=rows*intermediate) return;
        int source=i/intermediate*2*intermediate+i%intermediate; float x=up[source], gate=up[source+intermediate];
        float z=XMath.Abs(x)*0.7071067811865475f, t=1f/(1f+0.3275911f*z);
        float p=(((((1.061405429f*t-1.453152027f)*t)+1.421413741f)*t-0.284496736f)*t+0.254829592f)*t;
        float dp=((((5f*1.061405429f*t-4f*1.453152027f)*t)+3f*1.421413741f)*t-2f*0.284496736f)*t+0.254829592f;
        float exp=XMath.Exp(-z*z), erf=1f-p*exp; if(x<0) erf=-erf;
        float derivativeErf=exp*(dp*0.3275911f*t*t+2f*z*p)*0.7071067811865475f;
        dup[source]=dy[i]*gate*(0.5f*(1f+erf)+0.5f*x*derivativeErf);
        dup[source+intermediate]=dy[i]*0.5f*x*(1f+erf);
    }
    public static void ContextProbability(ArrayView<float> dc, ArrayView<float> v, ArrayView<float> dp, int rows, int heads, int dimension)
    {
        int i=Grid.GlobalIndex.X; if(i>=heads*rows*rows) return;
        int column=i%rows,row=i/rows%rows,head=i/(rows*rows); float sum=0;
        for(int d=0;d<dimension;d++) sum+=dc[(row*heads+head)*dimension+d]*v[(column*heads+head)*dimension+d];
        dp[i]=sum;
    }
    public static void ContextValue(ArrayView<float> p, ArrayView<float> dc, ArrayView<float> dv, int rows, int heads, int dimension)
    {
        int i=Grid.GlobalIndex.X; if(i>=rows*heads*dimension) return;
        int d=i%dimension, head=i/dimension%heads, column=i/(dimension*heads); float sum=0;
        for(int row=0;row<rows;row++) sum+=p[(head*rows+row)*rows+column]*dc[(row*heads+head)*dimension+d];
        dv[i]=sum;
    }
    public static void Softmax(ArrayView<float> p, ArrayView<float> gradient, int rows, int columns)
    {
        int row=Grid.GlobalIndex.X; if(row>=rows) return; int start=row*columns; float dot=0;
        for(int c=0;c<columns;c++) dot+=p[start+c]*gradient[start+c];
        for(int c=0;c<columns;c++) gradient[start+c]=p[start+c]*(gradient[start+c]-dot);
    }
    public static void AttentionQuery(ArrayView<float> ds, ArrayView<float> k, ArrayView<float> dq, int rows, int heads, int dimension)
    {
        int i=Grid.GlobalIndex.X; if(i>=rows*heads*dimension) return;
        int d=i%dimension,head=i/dimension%heads,row=i/(dimension*heads); float sum=0;
        for(int column=0;column<rows;column++) sum+=ds[(head*rows+row)*rows+column]*k[(column*heads+head)*dimension+d];
        dq[i]=sum/XMath.Sqrt((float)dimension);
    }
    public static void AttentionKey(ArrayView<float> ds, ArrayView<float> q, ArrayView<float> dk, int rows, int heads, int dimension)
    {
        int i=Grid.GlobalIndex.X; if(i>=rows*heads*dimension) return;
        int d=i%dimension,head=i/dimension%heads,column=i/(dimension*heads); float sum=0;
        for(int row=0;row<rows;row++) sum+=ds[(head*rows+row)*rows+column]*q[(row*heads+head)*dimension+d];
        dk[i]=sum/XMath.Sqrt((float)dimension);
    }
    public static void Rope(ArrayView<float> dq, ArrayView<float> dk, int rows, int heads, int dimension, float theta)
    {
        int i=Grid.GlobalIndex.X,half=dimension/2; if(i>=rows*heads*half) return;
        int pair=i%half,head=i/half%heads,position=i/(half*heads);
        float angle=position*XMath.Pow(theta,-2f*pair/dimension),cos=XMath.Cos(angle),sin=XMath.Sin(angle);
        int start=(position*heads+head)*dimension+pair;
        float q0=dq[start],q1=dq[start+half],k0=dk[start],k1=dk[start+half];
        dq[start]=q0*cos+q1*sin; dq[start+half]=q1*cos-q0*sin;
        dk[start]=k0*cos+k1*sin; dk[start+half]=k1*cos-k0*sin;
    }
    public static void Join(ArrayView<float> dq, ArrayView<float> dk, ArrayView<float> dv, ArrayView<float> fused, int rows, int hidden)
    {
        int i=Grid.GlobalIndex.X; if(i>=rows*hidden) return; int start=i/hidden*3*hidden+i%hidden;
        fused[start]=dq[i];fused[start+hidden]=dk[i];fused[start+2*hidden]=dv[i];
    }
    // Host schedules one work item per unique token/feature; repeated token rows
    // are reduced in fixed sequence order, without floating atomic races.
    public static void Embedding(ArrayView<int> ids, ArrayView<int> unique, ArrayView<float> dx, ArrayView<float> gradient, int rows, int uniqueCount, int hidden)
    {
        int i=Grid.GlobalIndex.X; if(i>=uniqueCount*hidden) return; int token=unique[i/hidden],d=i%hidden; float sum=0;
        for(int row=0;row<rows;row++) if(ids[row]==token) sum+=dx[row*hidden+d];
        gradient[token*hidden+d]+=sum;
    }
    public static void MarkerLoss(ArrayView<float> final, ArrayView<int> markers, ArrayView<float> weights,
        ArrayView<float> target, ArrayView<float> delta, ArrayView<float> loss, int candidates, int hidden, int type)
    {
        if(Grid.GlobalIndex.X!=0) return; float maximum=-float.MaxValue;
        for(int c=0;c<candidates;c++) {float sum=0;for(int d=0;d<hidden;d++) sum+=final[markers[c]*hidden+d]*weights[type*hidden+d];delta[c]=sum;maximum=XMath.Max(maximum,sum);}
        float sumExp=0,referenceLogit=0;
        for(int c=0;c<candidates;c++){referenceLogit+=target[c]*delta[c];delta[c]=XMath.Exp(delta[c]-maximum);sumExp+=delta[c];}
        loss[0]=maximum+XMath.Log(sumExp)-referenceLogit;
        for(int c=0;c<candidates;c++) delta[c]=delta[c]/sumExp-target[c];
    }
    public static void MarkerInput(ArrayView<int> markers, ArrayView<float> weights, ArrayView<float> delta, ArrayView<float> gradient, int rows, int candidates, int hidden, int type)
    {
        int i=Grid.GlobalIndex.X;if(i>=rows*hidden)return;int row=i/hidden;float sum=0;
        for(int c=0;c<candidates;c++)if(markers[c]==row)sum+=delta[c]*weights[type*hidden+i%hidden];
        gradient[i]=sum;
    }
    public static void MarkerWeight(ArrayView<float> final, ArrayView<int> markers, ArrayView<float> delta, ArrayView<float> gradient, int candidates, int hidden, int type)
    {
        int d=Grid.GlobalIndex.X;if(d>=hidden)return;float sum=0;
        for(int c=0;c<candidates;c++)sum+=delta[c]*final[markers[c]*hidden+d];
        gradient[type*hidden+d]+=sum;
    }
    public static void AdamW(ArrayView<float> weights, ArrayView<float> gradient, ArrayView<float> first, ArrayView<float> second,
        int count, float rate, float beta1, float beta2, float firstCorrection, float secondCorrection, float epsilon, float decay)
    {
        int i=Grid.GlobalIndex.X;if(i>=count)return;float g=gradient[i];
        float m=first[i]=beta1*first[i]+(1-beta1)*g, v=second[i]=beta2*second[i]+(1-beta2)*g*g;
        weights[i]=weights[i]*(1-rate*decay)-rate*(m/firstCorrection)/(XMath.Sqrt(v/secondCorrection)+epsilon);
    }
    public static void Finite(ArrayView<float> data,ArrayView<int> status,int count)
    {
        int block=Grid.GlobalIndex.X;if(block>=(count+4095)/4096)return;
        int stop=XMath.Min(count,(block+1)*4096),valid=1;
        for(int i=block*4096;i<stop;i++)if(!(data[i]>=-float.MaxValue&&data[i]<=float.MaxValue))valid=0;
        status[block]=valid;
    }
}
