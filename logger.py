import asyncio
import aiofiles
import json
import os
from datetime import datetime
from typing import Dict, Any
from pathlib import Path


class Logger:
    """
    비동기 로거
    
    - 이벤트 타입별 파일 생성
    - 엔진 결과 로깅
    - 파일 로테이션
    """
    
    def __init__(self, config):
        """
        Args:
            config: ConfigLoader 인스턴스
        """
        self.config = config
        self.log_directory = Path(config.get_log_dir())
        self.max_log_file_size = config.get_max_log_file_size() * 1024 * 1024
        self.max_log_files = config.get_max_log_files()
        
        # 파일 핸들러 (이벤트 타입별)
        self.event_files = {}  # {eventType: file_handle}
        self.result_file = None
        self.result_file_path = None
        
        # 로그 디렉토리 생성
        self.log_directory.mkdir(parents=True, exist_ok=True)
        
        # 잠금 (파일 작성 동기화)
        self.event_locks = {}  # {eventType: Lock}
        self.result_lock = asyncio.Lock()
    
    async def start(self):
        """로거 시작"""
        # 결과 로그 파일 생성
        await self._rotate_result_file()
        
        print(f'✓ Logger 시작됨 (로그 경로: {self.log_directory})')
        print(f'  - 입력 이벤트: raw_events_<EventType>_*.jsonl')
        print(f'  - 엔진 결과: engine_results_*.jsonl')
    
    async def stop(self):
        """로거 중지"""
        # 모든 이벤트 파일 닫기
        for file_handle in self.event_files.values():
            if file_handle:
                await file_handle.close()
        
        # 결과 파일 닫기
        if self.result_file:
            await self.result_file.close()
        
        print('✓ Logger 중지됨')
    
    async def log_event(self, event: Dict[str, Any]):
        """
        이벤트 로깅
        
        Args:
            event: 이벤트 데이터
        """
        try:
            event_type = event.get('eventType', 'Unknown')
            
            # 파일 핸들러 가져오기 (없으면 생성)
            if event_type not in self.event_files:
                await self._create_event_file(event_type)
            
            # 잠금 가져오기
            lock = self.event_locks.get(event_type)
            if not lock:
                return
            
            # JSON 변환
            log_line = json.dumps(event, ensure_ascii=False) + '\n'
            
            # 파일에 쓰기
            async with lock:
                file_handle = self.event_files.get(event_type)
                if file_handle:
                    await file_handle.write(log_line)
                    await file_handle.flush()
            
            # 파일 크기 확인 (로테이션)
            file_path = self.log_directory / f"raw_events_{event_type}_current.jsonl"
            if file_path.exists() and file_path.stat().st_size >= self.max_log_file_size:
                await self._rotate_event_file(event_type)
                
        except Exception as e:
            print(f'✗ 이벤트 로깅 오류: {e}')
    
    async def log_result(self, result: Dict[str, Any]):
        """
        엔진 결과 로깅
        
        Args:
            result: 엔진 처리 결과
        """
        try:
            # JSON 변환
            log_line = json.dumps(result, ensure_ascii=False) + '\n'
            
            # 파일에 쓰기
            async with self.result_lock:
                if self.result_file:
                    await self.result_file.write(log_line)
                    await self.result_file.flush()
            
            # 파일 크기 확인 (로테이션)
            if self.result_file_path.exists() and self.result_file_path.stat().st_size >= self.max_log_file_size:
                await self._rotate_result_file()
                
        except Exception as e:
            print(f'✗ 결과 로깅 오류: {e}')
    
    async def _create_event_file(self, event_type: str):
        """
        이벤트 타입별 파일 생성
        
        Args:
            event_type: 이벤트 타입
        """
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        filename = f'raw_events_{event_type}_{timestamp}.jsonl'
        file_path = self.log_directory / filename
        
        # 파일 열기
        file_handle = await aiofiles.open(file_path, 'a', encoding='utf-8')
        
        # 저장
        self.event_files[event_type] = file_handle
        self.event_locks[event_type] = asyncio.Lock()
        
        print(f'✓ 새 이벤트 로그 파일 생성 [{event_type}]: {filename}')
        
        # 오래된 파일 삭제
        await self._cleanup_old_logs(f'raw_events_{event_type}')
    
    async def _rotate_event_file(self, event_type: str):
        """
        이벤트 파일 로테이션
        
        Args:
            event_type: 이벤트 타입
        """
        # 기존 파일 닫기
        if event_type in self.event_files:
            file_handle = self.event_files[event_type]
            if file_handle:
                await file_handle.close()
        
        # 새 파일 생성
        await self._create_event_file(event_type)
        
        print(f'✓ 이벤트 로그 파일 로테이션 [{event_type}]')
    
    async def _rotate_result_file(self):
        """결과 로그 파일 로테이션"""
        # 기존 파일 닫기
        if self.result_file:
            await self.result_file.close()
        
        # 새 파일명 생성
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        filename = f'engine_results_{timestamp}.jsonl'
        self.result_file_path = self.log_directory / filename
        
        # 새 파일 열기
        self.result_file = await aiofiles.open(self.result_file_path, 'a', encoding='utf-8')
        
        print(f'✓ 새 결과 로그 파일 생성: {filename}')
        
        # 오래된 파일 삭제
        await self._cleanup_old_logs('engine_results')
    
    async def _cleanup_old_logs(self, prefix: str):
        """
        오래된 로그 파일 삭제
        
        Args:
            prefix: 파일명 접두사
        """
        # 로그 파일 목록 가져오기
        log_files = list(self.log_directory.glob(f'{prefix}_*.jsonl'))
        
        # 파일 개수가 제한을 초과하면 삭제
        if len(log_files) > self.max_log_files:
            # 생성 시간 기준으로 정렬 (오래된 것부터)
            log_files.sort(key=lambda f: f.stat().st_ctime)
            
            # 초과하는 파일 삭제
            files_to_delete = log_files[:len(log_files) - self.max_log_files]
            for file_path in files_to_delete:
                try:
                    file_path.unlink()
                    print(f'✓ 오래된 로그 파일 삭제: {file_path.name}')
                except Exception as e:
                    print(f'✗ 로그 파일 삭제 실패 ({file_path.name}): {e}')