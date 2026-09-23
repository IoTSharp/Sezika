using System.Text;

namespace Sezika.Cuda;

internal static class CudaPtx
{
    // These kernels are deliberately small correctness probes. They are fixed build
    // assets, not model supplied programs. The CUDA driver JITs PTX to the installed
    // device (the prototype targets PTX 7.0 and sm_70+).
    private static readonly byte[] VectorAddImage = Encoding.ASCII.GetBytes(
        """
        .version 7.0
        .target sm_70
        .address_size 64

        .visible .entry vector_add(
            .param .u64 vector_add_a,
            .param .u64 vector_add_b,
            .param .u64 vector_add_c,
            .param .u32 vector_add_n
        )
        {
            .reg .pred %p;
            .reg .b32 %r<6>;
            .reg .b64 %rd<7>;
            .reg .f32 %f<3>;

            ld.param.u64 %rd1, [vector_add_a];
            ld.param.u64 %rd2, [vector_add_b];
            ld.param.u64 %rd3, [vector_add_c];
            ld.param.u32 %r5, [vector_add_n];
            mov.u32 %r1, %ctaid.x;
            mov.u32 %r2, %ntid.x;
            mov.u32 %r3, %tid.x;
            mad.lo.u32 %r4, %r1, %r2, %r3;
            setp.ge.u32 %p, %r4, %r5;
            @%p bra VECTOR_DONE;
            mul.wide.u32 %rd4, %r4, 4;
            add.s64 %rd5, %rd1, %rd4;
            add.s64 %rd6, %rd2, %rd4;
            ld.global.f32 %f1, [%rd5];
            ld.global.f32 %f2, [%rd6];
            add.f32 %f0, %f1, %f2;
            add.s64 %rd5, %rd3, %rd4;
            st.global.f32 [%rd5], %f0;
        VECTOR_DONE:
            ret;
        }
        """ + "\0");

    internal static ReadOnlySpan<byte> VectorAdd => VectorAddImage;

    private static readonly byte[] GemmImage = Encoding.ASCII.GetBytes(
        """
        .version 7.0
        .target sm_70
        .address_size 64

        .visible .entry gemm(
            .param .u64 gemm_a,
            .param .u64 gemm_b,
            .param .u64 gemm_c,
            .param .u32 gemm_m,
            .param .u32 gemm_n,
            .param .u32 gemm_k
        )
        {
            .reg .pred %p<3>;
            .reg .b32 %r<15>;
            .reg .b64 %rd<10>;
            .reg .f32 %f<4>;

            ld.param.u64 %rd1, [gemm_a];
            ld.param.u64 %rd2, [gemm_b];
            ld.param.u64 %rd3, [gemm_c];
            ld.param.u32 %r5, [gemm_m];
            ld.param.u32 %r6, [gemm_n];
            ld.param.u32 %r7, [gemm_k];
            mov.u32 %r1, %ctaid.x;
            mov.u32 %r2, %ntid.x;
            mov.u32 %r3, %tid.x;
            mad.lo.u32 %r4, %r1, %r2, %r3;
            mov.u32 %r8, %ctaid.y;
            mov.u32 %r9, %ntid.y;
            mov.u32 %r10, %tid.y;
            mad.lo.u32 %r11, %r8, %r9, %r10;
            setp.ge.u32 %p1, %r4, %r5;
            @%p1 bra GEMM_DONE;
            setp.ge.u32 %p2, %r11, %r6;
            @%p2 bra GEMM_DONE;
            mov.u32 %r12, 0;
            mov.f32 %f0, 0f00000000;
        GEMM_LOOP:
            setp.ge.u32 %p1, %r12, %r7;
            @%p1 bra GEMM_STORE;
            mul.lo.u32 %r13, %r4, %r7;
            add.u32 %r13, %r13, %r12;
            mul.wide.u32 %rd4, %r13, 4;
            add.s64 %rd5, %rd1, %rd4;
            ld.global.f32 %f1, [%rd5];
            mul.lo.u32 %r13, %r12, %r6;
            add.u32 %r13, %r13, %r11;
            mul.wide.u32 %rd6, %r13, 4;
            add.s64 %rd7, %rd2, %rd6;
            ld.global.f32 %f2, [%rd7];
            fma.rn.f32 %f0, %f1, %f2, %f0;
            add.u32 %r12, %r12, 1;
            bra GEMM_LOOP;
        GEMM_STORE:
            mul.lo.u32 %r13, %r4, %r6;
            add.u32 %r13, %r13, %r11;
            mul.wide.u32 %rd8, %r13, 4;
            add.s64 %rd9, %rd3, %rd8;
            st.global.f32 [%rd9], %f0;
        GEMM_DONE:
            ret;
        }
        """ + "\0");

    internal static ReadOnlySpan<byte> Gemm => GemmImage;
}
