import json, os
p = r'D:\workspace\pkub\bee\SupportDocs\_retrieval.json'
d = json.load(open(p, encoding='utf-8'))
out = []
def walk(o, path=''):
    if isinstance(o, dict):
        if any(k in o for k in ('queryContains', 'answer', 'target', 'source', 'file')):
            out.append(repr({k: o[k] for k in ('queryContains', 'answer', 'target', 'source', 'file') if k in o}))
        for k, v in o.items():
            walk(v, path + '/' + k)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            walk(v, path)
walk(d)
# 只保留含关键映射的
kw = ['账上', '涨价', '料价', '报价', '新价', '业财', '一张表', '金蝶']
hits = [x for x in out if any(k in x for k in kw)]
open(r'D:\workspace\pkub\bee\.tmp-build\retr_chk.txt', 'w', encoding='utf-8').write(
    '\n'.join(hits) if hits else 'no keyword mapping found\n' + '\n'.join(out[:10]))
print('done')
