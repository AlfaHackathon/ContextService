"""Import the provided deidentified CSV as cited text, never as invented financial terms.

All nonidentical duplicate UIDs are withheld. Product flags do not create debts,
GROUP_ID does not create legal ownership, F↔U links do not create roles/grants.
The tool only talks to the configured local ContextService; model calls are absent.
"""
import argparse
import csv
import hashlib
import json
import os
from collections import defaultdict
from pathlib import Path
from urllib.request import Request, urlopen
from uuid import NAMESPACE_URL, uuid5


def company_id(uid):
    return str(uuid5(NAMESPACE_URL, 'alfahack:company:' + uid))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--companies', required=True)
    parser.add_argument('--links')
    parser.add_argument('--known-at', required=True, help='Explicit observation time with UTC offset')
    parser.add_argument('--url', default='http://127.0.0.1:5181/context-service')
    parser.add_argument('--limit', type=int)
    args = parser.parse_args()
    secret = os.environ.get('INTERNAL_SECRET')
    if not secret:
        raise RuntimeError('INTERNAL_SECRET required')
    from datetime import datetime
    if datetime.fromisoformat(args.known_at.replace('Z', '+00:00')).tzinfo is None:
        raise ValueError('known-at requires an explicit offset')

    def post(path, body):
        request = Request(args.url.rstrip('/') + '/internal/' + path,
                          data=json.dumps(body, ensure_ascii=False).encode('utf-8'),
                          headers={'Content-Type': 'application/json', 'X-Internal-Secret': secret})
        with urlopen(request, timeout=60) as response:
            return json.load(response)

    def material(identifier, title, text, companies, bank_ids):
        revision = 'row-' + hashlib.sha256(text.encode('utf-8')).hexdigest()[:32]
        result = post('material-versions', {'materialId': identifier, 'versionId': revision,
            'sourceId': 'provided-bank-csv', 'kind': 'company', 'title': title, 'text': text,
            'knownAt': args.known_at, 'publishedAt': None, 'correctsVersionId': None,
            'companyIds': companies, 'bankProfileIds': bank_ids})
        return {'materialId': identifier, 'versionId': revision, 'contentHash': result['contentHash']}

    rows = defaultdict(dict)
    with Path(args.companies).open(encoding='utf-8-sig', newline='') as file:
        for row in csv.DictReader(file, delimiter=';'):
            uid = row['SYNTHETIC_U']
            signature = json.dumps(row, ensure_ascii=False, sort_keys=True)
            rows[uid][signature] = row
    conflicts = sorted(uid for uid, values in rows.items() if len(values) != 1)
    eligible = sorted(uid for uid in rows if uid not in set(conflicts))
    selected = eligible if args.limit is None else eligible[:args.limit]
    imported = set()
    for uid in selected:
        row = next(iter(rows[uid].values()))
        target = company_id(uid)
        text = ('Обезличенный профиль предоставленного CSV. Поля ниже сообщены источником. '
                'Единицы и период REVENUE_COMPANY в CSV не установлены. Банковские продуктовые признаки '
                'не доказывают действующее обязательство, остаток, ставку или условия договора. '
                'GROUP_ID — банковский идентификатор группы, юридический контроль и правило консолидации неизвестны.\n'
                + '\n'.join(f'{key}: {value}' for key, value in row.items()))
        ref = material('bank-profile-' + uid, 'Профиль ' + uid, text, [target], [uid])
        exposures = [f'Банковский признак {key}; условия и текущее обязательство неизвестны'
                     for key in ('CL_PRODUCT_LOA_MARK', 'CL_PRODUCT_LEA_MARK', 'CL_PRODUCT_DEP_MARK', 'FTS_FLAG')
                     if row.get(key) in ('1', '1.0')]
        post('companies', {'companyId': target, 'revisionId': ref['versionId'], 'bankProfileId': uid,
            'name': 'Компания ' + uid,
            'description': '; '.join(filter(None, [row.get('MAINOKVED_NAME'), row.get('CITY'), row.get('REGION'), row.get('ORG_TYPE')])),
            'industryCodes': [row['MAINOKVED_CODE']] if row.get('MAINOKVED_CODE') else [],
            'exposures': exposures, 'knownAt': args.known_at, 'sourceRefs': [ref]})
        imported.add(uid)
        if len(imported) % 100 == 0:
            print(json.dumps({'importedCompanies': len(imported)}, ensure_ascii=False), flush=True)
    people = defaultdict(set)
    if args.links:
        with Path(args.links).open(encoding='utf-8-sig', newline='') as file:
            for row in csv.DictReader(file, delimiter=';'):
                if row['SYNTHETIC_U'] in imported:
                    people[row['SYNTHETIC_F']].add(row['SYNTHETIC_U'])
    person_count = 0
    for fid, uids in sorted(people.items()):
        if len(uids) > 512:
            continue
        targets = [company_id(uid) for uid in sorted(uids)]
        text = f'Обезличенное физлицо {fid}. CSV содержит связи с компаниями {", ".join(sorted(uids))}. '
        text += 'Вид отношений, должность, занятость, владение и права доступа этим файлом не установлены. Этот импорт может охватывать только выбранное подмножество компаний.'
        ref = material('bank-person-' + fid, 'Связи ' + fid, text, targets, sorted(uids))
        post('people', {'personId': fid, 'revisionId': ref['versionId'], 'knownAt': args.known_at,
            'companyLinks': [{'companyId': target, 'relationKind': 'unspecified', 'sourceRefs': [ref]} for target in targets],
            'sourceRefs': [ref]})
        person_count += 1
    print(json.dumps({'importedCompanies': len(imported), 'eligibleCompanies': len(eligible),
        'omittedByLimit': len(eligible) - len(imported), 'withheldConflictingUids': conflicts,
        'importedPeople': person_count, 'createdRoles': 0, 'createdLegalOwnershipEdges': 0}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
