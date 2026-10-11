"""Bounded offline arithmetic from pinned historical receipts; no GPU/network."""
import argparse, hashlib, json, math, pathlib, time
from bounded_start import wait_for_guardian

def main():
    wait_for_guardian();p=argparse.ArgumentParser();p.add_argument('--training-report',required=True);p.add_argument('--training-report-sha',required=True);p.add_argument('--profile',required=True);p.add_argument('--profile-sha',required=True);p.add_argument('--plan',required=True);p.add_argument('--plan-sha',required=True);p.add_argument('--updates',type=int,nargs='+',required=True);p.add_argument('--output',required=True);p.add_argument('--seconds',type=int,default=30);p.add_argument('--cancel');a=p.parse_args();started=time.monotonic()
    if not 1<=a.seconds<=180 or not 1<=len(a.updates)<=32 or any(not 1<=v<=800000000 for v in a.updates):raise ValueError('Explicit arithmetic/time bounds required')
    def check():
        if time.monotonic()-started>=a.seconds or (a.cancel and pathlib.Path(a.cancel).exists()):raise TimeoutError('Deadline/cancellation')
    def receipt(path,expected):
        check();source=pathlib.Path(path)
        if source.stat().st_size>1048576:raise ValueError('Small JSON bound1MiB')
        data=source.read_bytes();actual=hashlib.sha256(data).hexdigest()
        if len(expected)!=64 or actual!=expected:raise ValueError('Pinned receipt SHA differs')
        return json.loads(data),{'path':str(source.absolute()),'sha256':actual,'bytes':len(data)}
    training,training_ref=receipt(a.training_report,a.training_report_sha);profile,profile_ref=receipt(a.profile,a.profile_sha);plan,plan_ref=receipt(a.plan,a.plan_sha)
    if training['Status']!='PASS_experimental_GPU_updates_saved_only' or profile['Status']!='PASS_real_base_profile_only' or training['RunId']!=profile['RunId'] or plan['RunId']!=training['RunId'] or training['Inputs']!=profile['Inputs'] or training['Inputs']['PlanSha256']!=a.plan_sha or training['GpuUuid']!=profile['GpuUuid'] or plan['ExpectedGpuUuid']!=training['GpuUuid'] or training['CompletedUpdates']!=plan['MaximumUpdates']:raise ValueError('Historical receipt identities/denominators differ')
    count=training['CompletedUpdates']-training['InitialUpdates'];elapsed=float(training['ElapsedSeconds']);maximum_step=float(profile['MaximumStepSeconds'])
    if count<1 or not math.isfinite(elapsed) or elapsed<=0 or not math.isfinite(maximum_step) or maximum_step<=0:raise ValueError('Finite positive observed timing required')
    mean=elapsed/count;scenarios=[]
    for updates in a.updates:
        check();saves=1+updates//plan['CheckpointEvery'];reserve=600+120*saves
        scenarios.append({'updates':updates,'historical_report_amortized_seconds':mean*updates,'historical_report_amortized_days':mean*updates/86400,
                          'historical_max_step_seconds':maximum_step*updates,'historical_max_step_days':maximum_step*updates/86400,
                          'old_formula_conservative_seconds':4*maximum_step*updates+reserve,'old_formula_conservative_days':(4*maximum_step*updates+reserve)/86400,'hypothetical_checkpoint_every':plan['CheckpointEvery'],
                          'meaning':'scenario arithmetic only; no current-corpus throughput, convergence or cost prediction'})
    output=pathlib.Path(a.output)
    if output.exists():raise ValueError('Fresh estimate output required')
    result={'status':'HISTORICAL_ARITHMETIC_ONLY_NOT_CURRENT_TRAINING_BUDGET','references':{'training':training_ref,'profile':profile_ref,'plan':plan_ref},'historical_updates':count,'historical_elapsed_seconds':elapsed,'historical_report_seconds_per_update':mean,
            'historical_profile_maximum_step_seconds':maximum_step,'historical_profile_maximum_tokens':profile['MaximumTokensTested'],'historical_training_peak_owned_bytes':training['PeakOwnedBytes'],'historical_profile_peak_owned_bytes':profile['PeakOwnedBytes'],
            'hypothetical_updates_per_record':1,'scenarios':scenarios,'gpu_training_run':False,'current_profile_verified':False,'rental_price_queried':False,'rental_authorized_or_purchased':False}
    check()
    with output.open('x',encoding='utf-8') as writer:json.dump(result,writer,ensure_ascii=False,indent=2);writer.write('\n')
    for row in scenarios:print(row['updates'],round(row['historical_report_amortized_days'],2),round(row['historical_max_step_days'],2),round(row['old_formula_conservative_days'],2),flush=True)

if __name__=='__main__':main()
