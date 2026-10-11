"""Small semantic regression checks; no teacher or model execution."""
import argparse, pathlib, tempfile, types, unittest
from bounded_start import wait_for_guardian
from prepare_corpus import question, adapt, Writer, Budget, sha

class SemanticTests(unittest.TestCase):
    def test_target_dimensions_and_invalid_probabilities(self):
        for values in ([1.0],[-.1,1.1],[float('nan'),1.0],[.2,.3]):
            with self.assertRaises(ValueError):question('s','q','Boolean',['no','yes'],values)
    def test_boolq_false_is_first(self):
        q=list(adapt('google/boolq',{'passage':'p','question':'q','answer':False}))[0]
        self.assertEqual(q['Target'],[1.,0.]);self.assertEqual(q['Candidates'][0]['Id'],'false')
    def test_snli_missing_label_excluded(self):
        with self.assertRaises(ValueError):list(adapt('stanfordnlp/snli',{'premise':'p','hypothesis':'h','label':-1}))
    def test_helpsteer_complexity_not_correctness(self):
        row={'prompt':'p','response':'r',**{k:2 for k in ['helpfulness','correctness','coherence','complexity','verbosity']}}
        qs=list(adapt('nvidia/HelpSteer2',row));self.assertEqual(len(qs),5)
        self.assertIn('not answer quality',qs[3]['Instruction']);self.assertEqual(qs[3]['Target'],[0,0,1,0,0])
    def test_typed_boolean_reordered_by_id(self):
        row={'id':'1','state':'s','questions':{'q':{'type':'noul','instructions':'q','criteria':{'true':'yes','false':'no'}}},'gold':{'q':{'probabilities':{'true':.8,'false':.2}}}}
        q=list(adapt('LocalLLaMA/typed-decisions',row))[0];self.assertEqual(q['Target'],[.2,.8]);self.assertEqual(q['Candidates'][0]['Text'],'no')
    def test_preference_context_must_match(self):
        with self.assertRaises(ValueError):list(adapt('Anthropic/hh-rlhf',{'chosen':'p\n\nAssistant: good','rejected':'q\n\nAssistant: bad'}))
    def test_plain_typed_boolean_without_criteria(self):
        r={'id':'x','state':'s','questions':{'q':{'type':'noul','instructions':'statement'}},'gold':{'q':{'probabilities':{'true':.9,'false':.1}}}}
        self.assertEqual(list(adapt('LocalLLaMA/typed-decisions',r))[0]['Target'],[.1,.9])
    def test_quartz_answer_key(self):
        r={'para':'fact','para_id':'p','id':'x','question':'q','choices':{'label':['A','B'],'text':['first','second']},'answerKey':'B'}
        self.assertEqual(list(adapt('allenai/quartz',r))[0]['Target'],[0.,1.])
    def test_phase2_stockfish_reference_is_not_human_gold(self):
        r={'state':'position','instructions':'choose move','criteria_keys':['a','b'],'criteria_values':['move a','move b'],
           'label_alias':'b','meta':{'task_schema':'staged_v1','label_source':'stockfish'}}
        q=list(adapt('dwidlee/systemone-lite-phase2',r))[0]
        self.assertEqual(q['Target'],[0.,1.]);self.assertEqual(q['LabelKind'],'source_engine_reference')
        self.assertIn('not_teacher_accuracy',q['LabelSemantics'])
        r['meta']={'schema':'noul','task_schema':'staged_v1','label_source':'stockfish'}
        with self.assertRaises(ValueError):list(adapt('dwidlee/systemone-lite-phase2',r))
    def test_resume_keeps_closed_shard_bytes_and_appends_new_shard(self):
        import json,hashlib
        with tempfile.TemporaryDirectory(prefix='sezika-corpus-resume-test-',dir=self.fixture_root) as temp:
            root=pathlib.Path(temp);a=types.SimpleNamespace(seconds=30,cancel=None,maximum_records=20)
            w=Writer(root,Budget(a));asset={'path':'train.jsonl','sha256':'a'*64}
            w.add(question('s1','q','Boolean',['no','yes'],[1,0],family='f'),'s',asset,1,sha('row1'));w.close()
            first=root/w.shards[0]['path'];original=first.read_bytes()
            prior={'status':'PARTIAL_FAILED_PRESERVE_OUTPUTS','records_written':1,'shards':w.shards,'counts':{str(k):v for k,v in w.counts.items()}}
            next_writer=Writer(root,Budget(a),prior)
            next_writer.add(question('s2','q','Boolean',['no','yes'],[0,1],family='f'),'s',asset,2,sha('row2'));next_writer.close()
            self.assertEqual(original,first.read_bytes());self.assertEqual(len(next_writer.shards),2);self.assertEqual(next_writer.budget.records,2)
    def test_dedup_conflicts_exclude_all_and_rotation_preserves_answer(self):
        # Only this scoped temporary fixture is deleted by TemporaryDirectory.
        with tempfile.TemporaryDirectory(prefix='sezika-corpus-test-',dir=self.fixture_root) as temp:
            a=types.SimpleNamespace(seconds=30,cancel=None,maximum_records=20)
            w=Writer(pathlib.Path(temp),Budget(a));asset={'path':'train.jsonl','sha256':'a'*64}
            q=lambda t:question('state','q','Choice',['gold','wrong'],t,family='family')
            w.add(q([1.,0.]),'s',asset,1,sha('row'));w.add(q([1.,0.]),'s',asset,2,sha('row2'))
            w.add(q([0.,1.]),'s',asset,3,sha('row3'));w.add(q([1.,0.]),'s',asset,4,sha('row4'))
            self.assertEqual(w.budget.records,1);self.assertEqual(w.counts['duplicates'],1);self.assertEqual(w.counts['conflicts'],1);self.assertEqual(w.counts['quarantined'],2)
            w.close()
            import gzip,json
            p=pathlib.Path(temp)/w.shards[0]['path']
            with gzip.open(p,'rt',encoding='utf-8') as f:r=json.loads(f.readline())
            self.assertEqual(r['Candidates'][r['Target'].index(1.)]['Text'],'gold')
            self.assertEqual(len((pathlib.Path(temp)/'exclusions.jsonl').read_text().splitlines()),1)

if __name__=='__main__':
    wait_for_guardian();p=argparse.ArgumentParser();p.add_argument('--fixture-root',required=True);a=p.parse_args()
    SemanticTests.fixture_root=a.fixture_root
    unittest.main(argv=['test_corpus'],exit=True)
