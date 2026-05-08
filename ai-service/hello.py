"""
CCTV Guard — AI Microserver
Quick smoke-test: run this first to verify FastAPI + uvicorn work before
loading the heavy AI models (YOLOv8, DeepFace).
"""

from fastapi import FastAPI

app = FastAPI(
    title="CCTV Guard AI Microserver",
    description="Smoke-test endpoint — confirms the service is alive.",
    version="0.1.0",
)


@app.get("/")
def root():
    return {"message": "Hello from CCTV Guard AI Microserver!"}


@app.get("/health")
def health():
    return {
        "status": "ok",
        "service": "cctv-guard-ai",
        "version": "0.1.0",
        "model_loaded": False,   # will be True once main.py loads YOLOv8
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("hello:app", host="0.0.0.0", port=8000, reload=True)
