"""Privately normalize the two frozen ARC training parts, with identity bindings."""
import argparse, collections, hashlib, json, pathlib, re, shutil, time, unicodedata
from bounded_start import wait_for_guardian
from prepare_corpus import Budget, Writer, compact, onehot, question, sha, source_rows

SOURCE = 'allenai/ai2_arc'
REVISION = '210d026faf9955653af8916fad021475a3f00453'
TRAIN_SHAS = {
    'ARC-Challenge': 'e488c1587ffdcfc8443f916c53488a95cd471c5790e0746c6bfe4cecf20962cb',
    'ARC-Easy': 'b315db8a4be597dc7daa50a4e70d48dd7c990c32085629e6ccd8c926beaa80b5',
}
EXPECTED_ROWS = {'ARC-Challenge':1119, 'ARC-Easy':2251}
CARD_SHA = '4a293e352253d771604725c978adcea71ee6504e89ed40e1cb0024ecafa9b37e'


def normalized(value):
    if not isinstance(value,str):
        raise ValueError('String input expected')
    return ' '.join(unicodedata.normalize('NFC',value).split())


def canonical(row, family):
    choices = row['choices']
    labels = [str(value) for value in choices['label']]
    options = [normalized(value) for value in choices['text']]
    answer = str(row['answerKey'])
    if not 2 <= len(labels) <= 32 or len(set(labels)) != len(labels) or len(labels) != len(options) or answer not in labels:
        raise ValueError('choice_identity_or_answer_key')
    if any(not value for value in options) or len(set(options)) != len(options):
        raise ValueError('empty_or_duplicate_option_text')
    order = sorted(range(len(options)),key=lambda index: options[index])
    target_index = order.index(labels.index(answer))
    result = question(normalized(row['question']), 'Choose the correct answer to the science question.',
                      'Choice',[options[index] for index in order],onehot(len(order),target_index),
                      'source_annotation','en',family,
                      semantics='original_ARC_training_answer_key_not_teacher_probability_or_measured_accuracy')
    result['OriginalQuestionId'] = str(row['id'])
    result['SourceCandidateMapping'] = [{'CanonicalId':str(index),'OriginalLabel':labels[original],
                                         'OriginalText':choices['text'][original]} for index, original in enumerate(order)]
    result['SourceLicense'] = 'CC-BY-SA-4.0'
    return result


def self_test():
    row={'id':'x','question':' A\n B ','choices':{'label':['2','1'],'text':['z','a']},'answerKey':'2'}
    q=canonical(row,'family')
    assert q['State']=='A B' and q['Target']==[0.0,1.0]
    alternate={'id':'x','question':'A B','choices':{'label':['a','b'],'text':['a','z']},'answerKey':'b'}
    other=canonical(alternate,'family')
    assert q['Candidates']==other['Candidates'] and q['Target']==other['Target']
    row['answerKey']='missing'
    try:canonical(row,'family')
    except ValueError:pass
    else:raise AssertionError('unmatched label accepted')


def main():
    wait_for_guardian()
    ap=argparse.ArgumentParser()
    ap.add_argument('--inventory',required=True);ap.add_argument('--decoder',required=True)
    ap.add_argument('--output',required=True);ap.add_argument('--seconds',type=int,default=120)
    ap.add_argument('--cancel');ap.add_argument('--pilot-rows',type=int,default=0)
    a=ap.parse_args()
    if not 1<=a.seconds<=120 or not 0<=a.pilot_rows<=10:raise ValueError('Explicit ARC bounds required')
    a.maximum_records=4001;a.pilot_per_file=a.pilot_rows
    budget=Budget(a);self_test()
    inventory_path=pathlib.Path(a.inventory).resolve()
    if inventory_path.stat().st_size>8*1024*1024:raise ValueError('Inventory byte bound')
    inventory=json.loads(inventory_path.read_text(encoding='utf-8-sig'))
    if len(inventory)>5000:raise ValueError('Inventory item bound')
    selected=[x for x in inventory if x.get('dataset')==SOURCE and x.get('split_hint')=='source-train' and x['path'].endswith('.parquet')]
    cards=[x for x in inventory if x.get('dataset')==SOURCE and pathlib.Path(x['path']).name=='README.md']
    if len(selected)!=2 or len(cards)!=1:raise ValueError('Pinned two training assets and one card required')
    card=cards[0];card_path=pathlib.Path(card['path']).resolve()
    if card_path.stat().st_size>1024*1024:raise ValueError('Card byte bound')
    raw_card=card_path.read_bytes()
    if hashlib.sha256(raw_card).hexdigest()!=CARD_SHA or card['sha256']!=CARD_SHA or card['source_revision']!=REVISION:
        raise ValueError('Pinned source card differs')
    frontmatter=raw_card.decode('utf-8-sig').split('---',2)[1]
    if not re.search(r'(?m)^license:\s*\n- cc-by-sa-4\.0\s*$',frontmatter):
        raise ValueError('Frozen source license declaration not confirmed')
    source_assets=[]
    for asset in selected:
        budget.check();path=pathlib.Path(asset['path']).resolve();config=path.parent.name
        if config not in TRAIN_SHAS or asset['sha256']!=TRAIN_SHAS[config] or asset['source_revision']!=REVISION:
            raise ValueError('Pinned ARC training identity differs')
        if path.stat().st_size!=asset['bytes'] or path.stat().st_size>8*1024*1024:
            raise ValueError('ARC training byte identity/bound differs')
        if hashlib.sha256(path.read_bytes()).hexdigest()!=TRAIN_SHAS[config]:raise ValueError('ARC asset SHA differs')
        source_assets.append({k:asset.get(k) for k in ('dataset','path','sha256','bytes','source_revision','source_url','rows','split_hint')})
    out=pathlib.Path(a.output).resolve()
    if out.exists():raise ValueError('Preserve existing outputs')
    out.mkdir(parents=True)
    (out/'OWNED_BY_SEZIKA_CORPUS').write_text('Private ARC preparation; source training assets immutable; no publication authorization.\n',encoding='utf-8')
    notices=out/'source-notices';notices.mkdir()
    (notices/'UPSTREAM-README.md').write_bytes(raw_card)
    notice=(
        'Private Sezika ARC corpus preparation\n'
        'Source: Allen Institute for AI, allenai/ai2_arc\n'
        'Frozen source revision: '+REVISION+'\n'
        'Declared data license: Creative Commons Attribution-ShareAlike 4.0 International (CC-BY-SA-4.0).\n'
        'License reference: https://creativecommons.org/licenses/by-sa/4.0/\n'
        'Source reference: https://huggingface.co/datasets/allenai/ai2_arc/tree/'+REVISION+'\n'
        'Attribution: Peter Clark, Isaac Cowhey, Oren Etzioni, Tushar Khot, Ashish Sabharwal, Carissa Schoenick, Oyvind Tafjord (2018), Think you have Solved Question Answering? Try ARC, the AI2 Reasoning Challenge, arXiv:1803.05457.\n'
        'Changes: train-only extraction; Unicode NFC/whitespace normalization; candidate identities normalized through an explicit mapping; deterministic candidate rotation; exact-content deduplication; official-id/normalized-question family grouping; deterministic family development holdout.\n'
        'For any later sharing, preserve appropriate attribution, license reference and change notices; share adapted dataset material under CC-BY-SA-4.0, without extra restrictions.\n'
        'The present output remains private; this job does not publish or license an entire mixed corpus under these terms.\n'
        'Verification confirms frozen card declaration and byte identity. The card licensing narrative itself is incomplete; no independent ownership audit or legal conclusion is claimed.\n'
    )
    (notices/'SOURCE-NOTICE.txt').write_text(notice,encoding='utf-8')
    rights=[{'dataset':SOURCE,'revision':REVISION,'source_url':'https://huggingface.co/datasets/'+SOURCE,
             'card_license':'cc-by-sa-4.0','decision':'eligible_for_private_preparation_under_declared_cc_by_sa_4_0',
             'license_declaration_verified':True,'source_card_sha256':CARD_SHA,
             'attribution':'See source-notices/SOURCE-NOTICE.txt and byte-preserved upstream README citation',
             'derived_dataset_license':'CC-BY-SA-4.0','redistribution':'not_authorized_by_current_request',
             'later_sharing_obligations':['appropriate_attribution','license_reference','indicate_changes','adapted_dataset_sharealike','no_additional_restrictions'],
             'ownership_review':'not_independently_verified; upstream card narrative licensing section is incomplete',
             'formal_training_admitted':False}]
    (out/'source-rights.json').write_text(json.dumps(rights,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    source_assets.append({k:card.get(k) for k in ('dataset','path','sha256','bytes','source_revision','source_url','rows','split_hint')})
    (out/'source-assets.json').write_text(json.dumps(source_assets,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    decoder_logs=out/'decoder-logs';decoder_logs.mkdir()
    writer=Writer(out,budget);coverage=[{'source':SOURCE,'files':[],'status':'processed'}]
    status='PILOT_PREPARATION_ONLY' if a.pilot_rows else 'COMPLETE_PREPARATION_ONLY';failure=None
    buffered=[]
    parent={}
    def find(key):
        # At most 6,740 identity keys, hence this explicit worst-case chain bound.
        for _ in range(8000):
            budget.check()
            if parent[key]==key:return key
            parent[key]=parent[parent[key]];key=parent[key]
        raise ValueError('Family chain item bound')
    try:
        for asset in sorted(selected,key=lambda x:x['path']):
            budget.check();path=pathlib.Path(asset['path']);count=0
            for number,row in source_rows(path,a.decoder,budget,a.pilot_rows or 4000,decoder_logs):
                budget.check();count+=1
                if len(buffered)>=4000:raise ValueError('ARC buffered row bound')
                original='id:'+str(row['id']);state='question:'+sha(normalized(row['question']))
                for key in (original,state):
                    if key not in parent:parent[key]=key
                left=find(original);right=find(state)
                if left!=right:parent[max(left,right)]=min(left,right)
                buffered.append((asset,number,row,state))
            if not a.pilot_rows and count!=EXPECTED_ROWS[path.parent.name]:raise ValueError('Complete original train row count differs')
            coverage[0]['files'].append({'path':asset['path'],'sha256':asset['sha256'],'rows_read':count,'records_written':0,'pilot_prefix':bool(a.pilot_rows)})
            print(path.parent.name,'rows_read=',count,flush=True)
        emitted=collections.Counter()
        for asset,number,row,state in buffered:
            budget.check()
            try:
                before=budget.records;writer.add(canonical(row,find(state)),SOURCE,asset,number,sha(compact(row)))
                emitted[asset['path']]+=budget.records-before
            except (ValueError,KeyError,TypeError,IndexError) as error:writer.reject(SOURCE,asset['path'],number,str(error))
        for file in coverage[0]['files']:file['records_written']=emitted[file['path']]
    except Exception as error:
        status='PARTIAL_FAILED_PRESERVE_OUTPUTS';failure=repr(error);raise
    finally:
        writer.close()
        final_deadline=time.monotonic()+20
        def file_hash(path):
            if time.monotonic()>final_deadline:raise TimeoutError('ARC final hash deadline')
            if path.stat().st_size>16*1024*1024:raise ValueError('ARC final file byte bound')
            return hashlib.sha256(path.read_bytes()).hexdigest()
        for shard in writer.shards:
            path=out/shard['path'];shard.update(bytes=path.stat().st_size,sha256=file_hash(path))
        manifest={'schema_version':1,'status':status,'stage':1,'failure':failure,'pilot':bool(a.pilot_rows),
                  'formal_training_admitted':False,'token_length_admitted':False,'trainer_streaming_integration_accepted':False,
                  'new_training':False,'teacher_called':False,'original_split':'ARC-Challenge/train and ARC-Easy/train only; original validation/test untouched',
                  'derived_split':'official_id_OR_normalized_question_connected_families_sha_mod10; semantic_cross_source_review_pending',
                  'source_row_hash_semantics':'SHA256 canonical sorted UTF8 JSON of decoded row; not original file bytes',
                  'records_written':budget.records,'source_rows_read':len(buffered),'shards':writer.shards,'source_coverage':coverage,
                  'counts':{str(key):value for key,value in writer.counts.items()},'exclusions_mandatory':'exclusions.jsonl',
                  'elapsed_seconds':time.monotonic()-budget.start,'self_tests_passed':True,
                  'source_notice_bindings':{str(path.relative_to(out)):file_hash(path) for path in (notices/'UPSTREAM-README.md',notices/'SOURCE-NOTICE.txt')}}
        (out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        bindings={'ManifestSha256':file_hash(out/'manifest.json'),'ExclusionsSha256':file_hash(out/'exclusions.jsonl'),
                  'SourceRightsSha256':file_hash(out/'source-rights.json'),'SourceAssetsSha256':file_hash(out/'source-assets.json'),
                  'FormalTrainingAdmitted':False}
        (out/'corpus-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n',encoding='utf-8')
        print('status=',status,'records_written=',budget.records,'duplicates=',writer.counts['duplicates'],
              'conflicts=',writer.counts['conflicts'],'quarantined=',writer.counts['quarantined'],flush=True)


if __name__=='__main__':main()
