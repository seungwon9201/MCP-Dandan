"""
EventHub - Central event processing hub for 82ch

Processes events from Observer and routes them to detection engines.
No ZeroMQ - direct in-process communication.
"""

import asyncio
from typing import List, Optional, Dict, Any
from datetime import datetime


class EventHub:
    """
    Central event processing hub.

    Receives events from Observer, stores them in database,
    and routes them to detection engines for analysis.
    """

    def __init__(self, engines: List, db):
        self.engines = engines
        self.db = db
        self.running = False
        self.event_id_map = {}  # {event_ts: raw_event_id} - 이벤트와 결과 연결용

    async def start(self):
        """Start the EventHub."""
        self.running = True
        print('[EventHub] Started')

    async def stop(self):
        """Stop the EventHub."""
        self.running = False

        # Restore Claude config on shutdown
        import subprocess
        try:
            subprocess.run(['python', './transports/config_finder.py', '--restore'],
                         capture_output=True, text=True, timeout=10)
            print('[EventHub] Claude config restored')
        except Exception as e:
            print(f'[EventHub] Failed to restore config: {e}')

        print('[EventHub] Stopped')

    async def process_event(self, event: Dict[str, Any]) -> None:
        """
        Process a single event with optimized pipeline.

        1. Save event to database immediately (fast path)
        2. Launch engine analysis in background (non-blocking)

        Args:
            event: Event dictionary with eventType, producer, data, etc.
        """
        if not self.running:
            return

        try:
            # Step 1: 즉시 DB 저장 (빠른 응답)
            await self._save_event(event)

            # Step 2: 백그라운드에서 엔진 분석 실행 
            asyncio.create_task(self._analyze_event_async(event))

        except Exception as e:
            print(f'[EventHub] Error processing event: {e}')

    async def _analyze_event_async(self, event: Dict[str, Any]) -> None:
        """
        백그라운드에서 엔진 분석 수행 및 결과 일괄 저장.

        Args:
            event: 분석할 이벤트
        """
        try:
            # 관심 있는 엔진들에게 병렬로 작업 분배
            tasks = []
            for engine in self.engines:
                if engine.should_process(event):
                    task = self._process_with_engine(engine, event)
                    tasks.append(task)

            if not tasks:
                return

            # 모든 엔진 완료 대기
            results = await asyncio.gather(*tasks, return_exceptions=True)

            # 결과 수집
            all_results = []
            for result in results:
                if result and not isinstance(result, Exception):
                    if isinstance(result, list):
                        all_results.extend(result)
                    else:
                        all_results.append(result)

            # 결과 일괄 저장 (한 번의 트랜잭션)
            if all_results:
                await self._save_results_batch(all_results)

        except Exception as e:
            print(f'[EventHub] Error in async analysis: {e}')
            import traceback
            traceback.print_exc()

    async def _save_event(self, event: Dict[str, Any]):
        """Save event to database."""
        try:
            event_type = event.get('eventType', 'Unknown')

            # Save to raw_events table
            raw_event_id = await self.db.insert_raw_event(event)

            if raw_event_id and 'ts' in event:
                self.event_id_map[event['ts']] = raw_event_id

                # Save to type-specific tables
                if event_type.lower() in ['rpc', 'jsonrpc', 'mcp']:
                    await self.db.insert_rpc_event(event, raw_event_id)

                    # Extract MCP tool information if present
                    data = event.get('data', {})
                    message = data.get('message', {})
                    task = data.get('task', '')

                    if task == 'RECV' and 'tools' in message.get('result', {}):
                        count = await self.db.insert_mcpl()
                        if count and count > 0:
                            print(f'[EventHub] Extracted {count} tool(s) to mcpl table')

                            # mcpl에 insert된 tools를 백그라운드에서 분석
                            asyncio.create_task(self._analyze_mcpl_tools(count, event))

        except Exception as e:
            print(f'[EventHub] Error saving event: {e}')

    async def _save_results_batch(self, results: List[Dict[str, Any]]):
        """
        엔진 결과를 일괄 저장 (배치 처리).

        Args:
            results: 저장할 결과 리스트
        """
        try:
            saved_count = 0
            for result in results:
                result_data = result.get('result', {})
                original_event = result_data.get('original_event', {})

                # Map timestamp to raw_event_id
                raw_event_id = None
                if 'ts' in original_event:
                    raw_event_id = self.event_id_map.get(original_event['ts'])

                # Extract metadata
                server_name = original_event.get('mcpTag')
                producer = original_event.get('producer', 'unknown')

                # Save to DB
                engine_result_id = await self.db.insert_engine_result(
                    result, raw_event_id, server_name, producer
                )

                if engine_result_id:
                    saved_count += 1

            if saved_count > 0:
                print(f'[EventHub] Batch saved {saved_count} detection results')

        except Exception as e:
            print(f'[EventHub] Error in batch save: {e}')
            import traceback
            traceback.print_exc()

    async def _save_result(self, result: Dict[str, Any]):
        """Save single engine detection result to database (legacy method)."""
        try:
            # Map original event timestamp to raw_event_id
            raw_event_id = None
            result_data = result.get('result', {})
            original_event = result_data.get('original_event', {})

            if 'ts' in original_event:
                raw_event_id = self.event_id_map.get(original_event['ts'])

            # Extract server name and producer
            server_name = original_event.get('mcpTag')
            producer = original_event.get('producer', 'unknown')

            # Save engine result
            engine_result_id = await self.db.insert_engine_result(
                result, raw_event_id, server_name, producer
            )

            if engine_result_id:
                detector = result_data.get('detector')
                severity = result_data.get('severity')
                print(f'[EventHub] Saved detection result (id={engine_result_id}, detector={detector}, severity={severity}, server={server_name})')

        except Exception as e:
            print(f'[EventHub] Error saving result: {e}')

    async def _process_with_engine(self, engine, event: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        """Process event with a specific engine."""
        try:
            result = await engine.handle_event(event)
            return result
        except Exception as e:
            print(f'[EventHub] [{engine.name}] Error: {e}')
            return None

    async def _analyze_mcpl_tools(self, count: int, original_event: Dict[str, Any]):
        """
        mcpl에 insert된 tools를 조회하여 ToolsPoisoningEngine으로 전달

        Args:
            count: insert된 tool 개수
            original_event: 원본 이벤트 (mcpTag, producer 추출용)
        """
        try:
            # mcpl에서 최근 insert된 tools 조회
            tools = await self.db.get_recent_mcpl_tools(limit=count)

            if not tools:
                print(f'[EventHub] No tools found in mcpl table')
                return

            print(f'[EventHub] Analyzing {len(tools)} tools with ToolsPoisoningEngine')

            # ToolsPoisoningEngine 찾기
            tools_poisoning_engine = None
            for engine in self.engines:
                if engine.name == 'ToolsPoisoningEngine':
                    tools_poisoning_engine = engine
                    break

            if not tools_poisoning_engine:
                print(f'[EventHub] ToolsPoisoningEngine not found')
                return

            # 각 tool에 대해 병렬 분석 수행
            tasks = []
            for tool_data in tools:
                # tool_data를 event 형식으로 변환
                synthetic_event = {
                    'eventType': 'MCP',
                    'producer': tool_data.get('producer', 'unknown'),
                    'mcpTag': tool_data.get('mcpTag', 'unknown'),
                    'ts': original_event.get('ts'),  # 원본 이벤트의 timestamp 사용
                    'data': {
                        'task': 'RECV',
                        'message': {
                            'result': {
                                'tools': [{
                                    'name': tool_data.get('tool'),
                                    'description': tool_data.get('tool_description')
                                }]
                            }
                        },
                        'mcpTag': tool_data.get('mcpTag', 'unknown')
                    }
                }

                # 병렬 처리를 위해 task 추가
                task = self._process_with_engine(tools_poisoning_engine, synthetic_event)
                tasks.append(task)

            # 모든 분석 완료 대기
            results = await asyncio.gather(*tasks, return_exceptions=True)

            # 결과 수집
            all_results = []
            for result in results:
                if result and not isinstance(result, Exception):
                    if isinstance(result, list):
                        all_results.extend(result)
                    else:
                        all_results.append(result)

            # 결과 일괄 저장
            if all_results:
                await self._save_results_batch(all_results)

        except Exception as e:
            print(f'[EventHub] Error analyzing mcpl tools: {e}')