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
        print('[EventHub] Stopped')

    async def process_event(self, event: Dict[str, Any]) -> None:
        """
        Process a single event synchronously.

        1. Save event to database
        2. Route to all interested engines
        3. Save engine results to database

        Args:
            event: Event dictionary with eventType, producer, data, etc.
        """
        if not self.running:
            return

        try:
            # Save event to database
            await self._save_event(event)

            # Route to engines
            tasks = []
            for engine in self.engines:
                # Check if engine is interested in this event
                if engine.should_process(event):
                    task = self._process_with_engine(engine, event)
                    tasks.append(task)

            # Wait for all engines to process
            if tasks:
                results = await asyncio.gather(*tasks, return_exceptions=True)

                # Save results
                for result in results:
                    if result and not isinstance(result, Exception):
                        await self._save_result(result)

        except Exception as e:
            print(f'[EventHub] Error processing event: {e}')

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

                elif event_type.lower() in ['file', 'fileio']:
                    await self.db.insert_file_event(event, raw_event_id)
                elif event_type.lower() == 'process':
                    await self.db.insert_process_event(event, raw_event_id)

        except Exception as e:
            print(f'[EventHub] Error saving event: {e}')

    async def _save_result(self, result: Dict[str, Any]):
        """Save engine detection result to database."""
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

            if not engine_result_id:
                print(f'[EventHub] Failed to save engine result')
                return

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