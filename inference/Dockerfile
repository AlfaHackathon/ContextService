FROM python:3.12-slim-bookworm
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY --chmod=0444 server.py healthcheck.py ./
USER 1654:1654
# Shares ContextService's network namespace because server.py binds only loopback.
HEALTHCHECK --interval=60s --timeout=30s --start-period=120s --retries=3 \
    CMD ["python", "healthcheck.py"]
ENTRYPOINT ["python", "server.py", "--models", "/models", "--port", "5183"]
