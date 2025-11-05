from abc import ABC, abstractmethod
from typing import Any


class BaseEngine(ABC):

    def __init__(self, db, name: str, event_types: list[str] | None = None):
        self.db = db
        self.name = name
        self.event_types = event_types or []

    def should_process(self, data: dict) -> bool:
        if not self.event_types:
            return True  # 모든 이벤트 처리
        return data.get("eventType") in self.event_types
    @abstractmethod
    def process(self, data: Any) -> Any:
        raise NotImplementedError

    async def handle_event(self, data: Any):
        # 이벤트 타입 필터링
        if self.event_types and data.get('eventType') not in self.event_types:
            return None

        try:
            result = self.process(data)
            return result
        except Exception as e:
            try:
                print(f"[{self.name}] ERROR during processing: {e}")
            except Exception:
                pass
            return None
