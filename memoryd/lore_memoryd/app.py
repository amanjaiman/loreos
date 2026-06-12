"""FastAPI application factory for memoryd."""

from fastapi import FastAPI


def create_app() -> FastAPI:
    """Build the memoryd application. Spec 001: health check only."""
    app = FastAPI(title="lore-memoryd", docs_url=None, redoc_url=None)

    @app.get("/health")
    def health() -> dict[str, str]:
        """Liveness probe used by the supervising agent (spec 003+)."""
        return {"status": "ok"}

    return app


app = create_app()
