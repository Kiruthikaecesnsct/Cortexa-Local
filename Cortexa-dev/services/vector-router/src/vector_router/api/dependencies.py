from fastapi import Request

from ..application.router import VectorRouter


def get_router(request: Request) -> VectorRouter:
    return request.app.state.vector_router
