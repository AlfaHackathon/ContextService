"""Readiness requires both supplied ONNX models to perform real inference."""
import json
import math
from urllib.request import Request, urlopen


def post(path, body):
    request = Request('http://127.0.0.1:5183/v1/' + path,
                      data=json.dumps(body).encode(),
                      headers={'Content-Type': 'application/json'})
    with urlopen(request, timeout=25) as response:
        return json.load(response)


def main():
    embedded = post('embeddings', {'model': 'multilingual-e5-small', 'input': ['query: readiness']})
    vector = embedded['data'][0]['embedding']
    if len(vector) != 384 or not all(math.isfinite(x) for x in vector):
        raise ValueError('Invalid embedding output')
    ranked = post('rerank', {'model': 'mmarco-reranker', 'query': 'readiness',
                             'documents': ['readiness'], 'top_n': 1})
    if len(ranked['results']) != 1 or not math.isfinite(ranked['results'][0]['relevance_score']):
        raise ValueError('Invalid reranker output')


if __name__ == '__main__':
    main()
