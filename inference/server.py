"""Local CPU inference sidecar. Domain state, scopes and retrieval stay in .NET.

Requires numpy, onnxruntime and tokenizers; weights are supplied by MODEL_DIRECTORY.
No downloads, cloud calls, research imports or output caches.
"""
import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Lock

import numpy as np
import onnxruntime as ort
from tokenizers import Tokenizer


class Models:
    def __init__(self, directory, threads):
        self.directory = Path(directory)
        self.threads = threads
        self.loaded = {}

    def load(self, name):
        if name not in self.loaded:
            folder = self.directory / name
            options = ort.SessionOptions()
            options.intra_op_num_threads = self.threads
            options.inter_op_num_threads = 1
            options.log_severity_level = 3
            session = ort.InferenceSession(str(folder / 'model.onnx'), sess_options=options,
                                          providers=['CPUExecutionProvider'])
            tokenizer = Tokenizer.from_file(str(folder / 'tokenizer.json'))
            tokenizer.enable_truncation(max_length=512)
            tokenizer.enable_padding(pad_id=1, pad_token='<pad>')
            self.loaded[name] = session, tokenizer
        return self.loaded[name]

    @staticmethod
    def feed(session, encoded):
        available = {'input_ids': np.asarray([x.ids for x in encoded], dtype=np.int64),
                     'attention_mask': np.asarray([x.attention_mask for x in encoded], dtype=np.int64),
                     'token_type_ids': np.asarray([x.type_ids for x in encoded], dtype=np.int64)}
        return {item.name: available[item.name] for item in session.get_inputs()}

    def embed(self, texts):
        session, tokenizer = self.load('e5_small')
        output = []
        for start in range(0, len(texts), 8):
            batch = texts[start:start + 8]
            feed = self.feed(session, tokenizer.encode_batch(batch))
            hidden = session.run(None, feed)[0]
            mask = feed['attention_mask'][..., None]
            pooled = (hidden * mask).sum(axis=1) / mask.sum(axis=1).clip(min=1)
            pooled /= np.linalg.norm(pooled, axis=1, keepdims=True).clip(min=1e-12)
            output.extend(pooled.astype(np.float32).tolist())
        return output

    def rerank(self, query, texts):
        session, tokenizer = self.load('mmarco_reranker')
        output = []
        for start in range(0, len(texts), 8):
            encoded = tokenizer.encode_batch([(query, text) for text in texts[start:start + 8]])
            output.extend(session.run(None, self.feed(session, encoded))[0].reshape(-1).tolist())
        return output


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--models', required=True)
    parser.add_argument('--port', type=int, default=5183)
    parser.add_argument('--threads', type=int, default=4)
    args = parser.parse_args()
    models = Models(args.models, args.threads)
    inference_lock = Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.1'
        def respond(self, status, value):
            data = json.dumps(value, ensure_ascii=False, allow_nan=False).encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            if self.path == '/health':
                self.respond(200, {'status': 'ok', 'models': ['multilingual-e5-small', 'mmarco-reranker'],
                                   'device': 'cpu'})
            else:
                self.respond(404, {'error': 'not_found'})

        def do_POST(self):
            try:
                if self.headers.get('Transfer-Encoding', '').lower() == 'chunked':
                    chunks = []
                    total = 0
                    while True:
                        line = self.rfile.readline(100)
                        length = int(line.split(b';', 1)[0].strip(), 16)
                        if length == 0:
                            while self.rfile.readline(8192).strip():
                                pass
                            break
                        total += length
                        if length < 0 or total > 1500000:
                            raise ValueError('body budget exceeded')
                        chunks.append(self.rfile.read(length))
                        if self.rfile.read(2) != b'\r\n':
                            raise ValueError('Invalid chunk framing')
                    raw = b''.join(chunks)
                else:
                    length = int(self.headers.get('Content-Length', '0'))
                    if length < 1 or length > 1500000:
                        self.respond(413, {'error': 'body_budget'})
                        return
                    raw = self.rfile.read(length)
                body = json.loads(raw)
                texts = body.get('input' if self.path == '/v1/embeddings' else 'documents')
                if (not isinstance(texts, list) or not 1 <= len(texts) <= 256
                        or any(not isinstance(x, str) or not x.strip() or len(x) > 20000 for x in texts)):
                    raise ValueError('Expected 1–256 bounded nonempty texts')
                if self.path == '/v1/embeddings':
                    if body.get('model') != 'multilingual-e5-small':
                        raise ValueError('Unknown embedding model')
                    if any(not x.startswith(('query: ', 'passage: ')) for x in texts):
                        raise ValueError('E5 requires explicit query/passsage prefixes')
                    with inference_lock:
                        vectors = models.embed(texts)
                    self.respond(200, {'object': 'list', 'model': body['model'], 'data': [
                        {'object': 'embedding', 'index': i, 'embedding': vector}
                        for i, vector in enumerate(vectors)]})
                elif self.path == '/v1/rerank':
                    if body.get('model') != 'mmarco-reranker' or not isinstance(body.get('query'), str) or len(body['query']) > 16000:
                        raise ValueError('Unknown reranker or invalid query')
                    with inference_lock:
                        scores = models.rerank(body['query'], texts)
                    top = int(body.get('top_n', len(texts)))
                    self.respond(200, {'results': sorted([
                        {'index': i, 'relevance_score': score} for i, score in enumerate(scores)],
                        key=lambda x: x['relevance_score'], reverse=True)[:top]})
                else:
                    self.respond(404, {'error': 'not_found'})
            except (ValueError, KeyError, TypeError, json.JSONDecodeError):
                self.respond(400, {'error': 'invalid_inference_request'})
            except Exception as error:
                print('inference_failed', type(error).__name__, flush=True)
                self.respond(503, {'error': 'local_inference_unavailable'})

    print(f'Local retrieval inference listening on 127.0.0.1:{args.port}', flush=True)
    class Server(ThreadingHTTPServer):
        daemon_threads = True
        request_queue_size = 64
    Server(('127.0.0.1', args.port), Handler).serve_forever()


if __name__ == '__main__':
    main()
