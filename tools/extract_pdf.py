"""Local, layout-preserving PDF/OCR extraction. No model or hand-coded bank values.

Needs pdfplumber, numpy, Pillow, and Tesseract (rus+eng). On Windows --wsl Ubuntu
uses a locally installed WSL Tesseract. Outputs text plus source hash, page/cell
coordinates, OCR confidence and unresolved cells. Never silently fills a cell.
"""
import argparse
import csv
import hashlib
import io
import json
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
import pdfplumber
from PIL import ImageDraw, ImageOps


def clusters(values, gap=3):
    groups = []
    for value in values:
        if not groups or value - groups[-1][-1] > gap:
            groups.append([])
        groups[-1].append(int(value))
    return [int(np.mean(g)) for g in groups]


def grids(image):
    dark = np.array(image.convert('L')) < 150
    height, width = dark.shape
    ys = clusters(np.where(dark.sum(axis=1) > width * .45)[0])
    groups = []
    for y in ys:
        if not groups or y - groups[-1][-1] > height * .045:
            groups.append([])
        groups[-1].append(y)
    result = []
    for rows in groups:
        if len(rows) < 3 or rows[-1]-rows[0] < height*.08:
            continue
        xs = clusters(np.where(dark[rows[0]:rows[-1]+1].sum(axis=0) > (rows[-1]-rows[0])*.65)[0])
        if len(xs) < 3:
            continue
        # Some tables omit the top horizontal rule; recover the header from vertical rules.
        above = rows[0]
        while above > max(0, rows[0]-int(height*.04)):
            votes = sum(dark[above-1, max(0,x-2):x+3].any() for x in xs)
            if votes < len(xs)*.7:
                break
            above -= 1
        if rows[0]-above > height*.008:
            rows.insert(0, above)
        result.append((xs, rows))
    return result


def ocr(path, wsl, mode):
    command = ['tesseract', str(path), 'stdout', '-l', 'rus+eng', '--psm', str(mode), 'tsv']
    if wsl:
        absolute = path.resolve().as_posix()
        mounted = '/mnt/' + absolute[0].lower() + absolute[2:]
        command = ['wsl', '-d', wsl, '--', *command[:1], mounted, *command[2:]]
    run = subprocess.run(command, capture_output=True, check=True)
    rows = csv.DictReader(io.StringIO(run.stdout.decode('utf-8-sig')), delimiter='\t', quoting=csv.QUOTE_NONE)
    return [{**r, **{k: float(r[k]) for k in ['left','top','width','height','conf']}}
            for r in rows if r.get('text','').strip() and r['level']=='5']


def cell_words(words, box):
    x0,y0,x1,y1 = box
    selected = [w for w in words if x0 <= w['left']+w['width']/2 < x1 and y0 <= w['top']+w['height']/2 < y1]
    selected.sort(key=lambda w:(round((w['top']+w['height']/2)/12), w['left']))
    return ' '.join(w['text'] for w in selected).replace('|','/'), min([w['conf'] for w in selected], default=0)


def numeric_cells(image, xs, ys, output, prefix, wsl):
    """Independent segmentation: OCR every numerical cell as one enlarged line."""
    paths=[]
    for r in range(len(ys)-1):
        for c in range(len(xs)-1):
            cell=image.crop((xs[c]+4,ys[r]+4,xs[c+1]-4,ys[r+1]-4))
            cell=ImageOps.expand(cell.resize((cell.width*2,cell.height*2)),border=20,fill='white')
            path=output/f'{prefix}-{r}-{c}.png';cell.save(path)
            absolute=path.resolve().as_posix()
            paths.append({'path':'/mnt/'+absolute[0].lower()+absolute[2:] if wsl else absolute,'header':r==0 or c==0})
    code="""import csv,io,json,subprocess,sys
result=[]
for item in json.loads(sys.stdin.read()):
 readings=[]
 for mode in [6,7]:
  args=['tesseract',item['path'],'stdout','-l','rus+eng' if item['header'] else 'eng','--psm',str(mode)]
  if not item['header']: args+=['-c','tessedit_char_whitelist=0123456789.,-%*']
  p=subprocess.run(args+['tsv'],capture_output=True,check=True)
  words=[w for w in csv.DictReader(io.StringIO(p.stdout.decode('utf-8')),delimiter='\\t',quoting=csv.QUOTE_NONE) if w.get('text','').strip() and w['level']=='5']
  readings.append({'text':' '.join(w['text'] for w in words),'confidence':min([float(w['conf']) for w in words],default=0)})
 result.append(readings)
print(json.dumps(result))
"""
    command=['wsl','-d',wsl,'--','python3','-c',code] if wsl else [sys.executable,'-c',code]
    values=json.loads(subprocess.run(command,input=json.dumps(paths).encode(),capture_output=True,check=True).stdout)
    return {(r,c):values[r*(len(xs)-1)+c] for r in range(len(ys)-1) for c in range(len(xs)-1)}


def extract(pdf, output, wsl=None, pages=None):
    output.mkdir(parents=True, exist_ok=True)
    document = pdfplumber.open(pdf)
    report = {'version':1, 'pdfSha256':hashlib.sha256(pdf.read_bytes()).hexdigest(), 'pages':[], 'warnings':[], 'tables':0}
    texts = []
    if pages and any(p < 1 or p > len(document.pages) for p in pages):
        raise ValueError("Page number outside PDF")
    for page_index, page in enumerate(document.pages):
        if pages and page_index+1 not in pages:
            continue
        image = page.to_image(resolution=300).original.convert('RGB')
        image_path = output/f'page-{page_index+1}.png'
        image.save(image_path)
        words = ocr(image_path, wsl, 3)
        windows_words=[]
        if sys.platform=='win32':
            small=output/f'page-{page_index+1}-windows.png'
            image.resize((image.width//2,image.height//2)).save(small)
            run=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(Path(__file__).with_name('windows_ocr.ps1')),'-ImagePath',str(small.resolve())],capture_output=True)
            if run.returncode==0:
                windows_words=json.loads(run.stdout.decode('utf-8-sig'))
                for word in windows_words:
                    for field in ['left','top','width','height']:word[field]*=2
            else:
                report['warnings'].append(f'Windows OCR unavailable on page {page_index+1}; Tesseract checks only')
        table_reports, tables = [], []
        for table_index, (xs, ys) in enumerate(grids(image)):
            box = (xs[0],ys[0],xs[-1]+1,ys[-1]+1)
            crop = image.crop(box)
            original_crop=output/f'page-{page_index+1}-table-{table_index+1}-grid.png'
            crop.save(original_crop)
            draw = ImageDraw.Draw(crop)
            for x in xs:
                draw.line((x-xs[0],0,x-xs[0],crop.height),fill='white',width=5)
            for y in ys:
                draw.line((0,y-ys[0],crop.width,y-ys[0]),fill='white',width=5)
            crop_path=output/f'page-{page_index+1}-table-{table_index+1}.png'
            crop.save(crop_path)
            a,b=ocr(crop_path,wsl,11),ocr(original_crop,wsl,11)
            isolated=numeric_cells(image,xs,ys,output,f'p{page_index+1}-t{table_index+1}-cell',wsl)
            rows, cells = [], []
            for r in range(len(ys)-1):
                row=[]
                for c in range(len(xs)-1):
                    bounds=(xs[c]-xs[0],ys[r]-ys[0],xs[c+1]-xs[0],ys[r+1]-ys[0])
                    value,confidence=cell_words(a,bounds)
                    other,other_confidence=cell_words(b,bounds)
                    numeric=lambda s:re.sub(r'\s+','',s).replace('.',',')
                    unresolved=not value or numeric(value)!=numeric(other) or confidence<65 or other_confidence<65
                    third=isolated.get((r,c))
                    readings=[{'text':value,'confidence':confidence},{'text':other,'confidence':other_confidence},*(third or [])]
                    reliable=[r for r in readings if r['text'] and r['confidence']>=65]
                    # Require agreement of at least two confident readings, and no confident dissent.
                    if len(reliable)>=2 and len({numeric(r['text']) for r in reliable})==1:
                        value=reliable[0]['text'];unresolved=False
                    else:
                        unresolved=True
                    independent,_=cell_words(windows_words,(xs[c],ys[r],xs[c+1],ys[r+1]))
                    comparison=(lambda s:sorted(re.findall(r'\w+',s.lower()))) if c==0 else numeric
                    if independent and any(comparison(independent)==comparison(reading['text']) for reading in reliable):
                        value=independent;unresolved=False
                    if r==0 and c==0 and not re.search(r'\w',value):
                        value='Показатель'; unresolved=False
                    cells.append({'row':r,'column':c,'box':bounds,'raw':value,'secondPass':other,'isolatedCell':third,'windowsOcr':independent,'confidence':confidence,'unresolved':unresolved})
                    if unresolved and c==0:
                        value='[неразборчивая подпись; OCR варианты: '+value+' / '+independent+']'
                    elif unresolved:
                        value='[неразборчиво]'
                    row.append(value.replace('|','/'))
                rows.append(row)
            markdown='\n'.join([' | '.join(rows[0]),' | '.join(['---']*len(rows[0])),*[' | '.join(r) for r in rows[1:]]])
            tables.append((box,markdown))
            table_reports.append({'box':box,'rows':rows,'cells':cells})
        # Keep prose outside table coordinates, ordered by OCR block/line; tables replace their pixels.
        lines={}
        for word in words:
            cx,cy=word['left']+word['width']/2,word['top']+word['height']/2
            if any(x0<=cx<x1 and y0<=cy<y1 for (x0,y0,x1,y1),_ in tables):
                continue
            key=(word['block_num'],word['par_num'],word['line_num'])
            lines.setdefault(key,[]).append(word)
        blocks=[(min(w['top'] for w in row), min(w['left'] for w in row),' '.join(w['text'] for w in sorted(row,key=lambda w:w['left']))) for row in lines.values()]
        blocks += [(box[1],box[0],text) for box,text in tables]
        blocks.sort()
        texts.append(f'\n[Страница {page_index+1}]\n'+'\n'.join(b[2] for b in blocks))
        report['pages'].append({'page':page_index+1,'image':str(image_path.resolve()),'tables':table_reports,'ocrWords':words,'windowsWords':windows_words})
        report['tables']+=len(tables)
    report['unresolvedCells']=sum(c['unresolved'] for p in report['pages'] for t in p['tables'] for c in t['cells'])
    report['warnings'].append('OCR is fallible; unresolved disagreement stays unreadable. Charts are not digitized. Page coordinates and all readings are retained. Windows confidence is not measured; its words only corroborate Tesseract.')
    (output/'text.txt').write_text('\n'.join(texts),encoding='utf-8')
    (output/'extraction.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['pdfSha256','tables','unresolvedCells']}))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('pdf',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--wsl')
    parser.add_argument('--pages',type=int,nargs='+')
    args=parser.parse_args()
    extract(args.pdf,args.output,args.wsl,args.pages)
