"""
데이터베이스 조회 스크립트
사용법: python query_db.py
"""

import asyncio
import json
from database import Database
from datetime import datetime


async def main():
    # 데이터베이스 연결
    db = Database()
    await db.connect()

    print("=" * 80)
    print("82ch MCP Observer - Database Query Tool")
    print("=" * 80)
    print()

    # 1. 통계 조회
    print("📊 전체 통계:")
    print("-" * 80)
    stats = await db.get_event_statistics()
    print(f"총 이벤트 수: {stats.get('total_events', 0):,}")
    print(f"탐지된 이벤트 수: {stats.get('detected_events', 0):,}")
    print()

    print("이벤트 타입별 통계:")
    by_type = stats.get('by_type', {})
    for event_type, count in sorted(by_type.items(), key=lambda x: x[1], reverse=True):
        print(f"  {event_type:20s}: {count:,}")
    print()

    # 2. 최근 이벤트 조회
    print("📝 최근 이벤트 (10개):")
    print("-" * 80)
    recent = await db.get_recent_events(limit=10)
    for event in recent:
        # 타임스탬프 변환 (.NET ticks to datetime)
        ts = event['ts']
        try:
            # .NET ticks를 Unix timestamp로 변환
            unix_timestamp = (ts / 10000000) - 62135596800
            dt = datetime.fromtimestamp(unix_timestamp)
            time_str = dt.strftime('%Y-%m-%d %H:%M:%S')
        except (OSError, ValueError):
            # 타임스탬프 변환 실패 시 원본 값 표시
            time_str = f"ts={ts}"

        print(f"[{event['id']:4d}] {time_str} | "
              f"{event['event_type']:15s} | {event['producer']:8s}")
    print()

    # 3. Semantic Gap 고득점 조회
    print("🎯 Semantic Gap 고득점 결과:")
    print("-" * 80)
    high_scores = await db.get_high_semantic_gap_results(threshold=70, limit=10)
    if high_scores:
        for result in high_scores:
            score = result.get('final_score', 0)
            event_type = result.get('event_type', 'Unknown')
            print(f"점수: {score:3d} | 타입: {event_type}")
    else:
        print("  (결과 없음)")
    print()

    # 4. RPC Request-Response 통계
    print("🔌 RPC Request-Response 통계:")
    print("-" * 80)

    # 먼저 initialize 응답으로부터 pid -> 서버 이름 매핑 생성
    # Request의 PID를 Response와 매칭
    pid_to_server = {}
    async with db.conn.execute(
        """
        SELECT raw_req.pid, rpc_resp.result
        FROM rpc_events rpc_req
        JOIN raw_events raw_req ON rpc_req.raw_event_id = raw_req.id
        JOIN rpc_events rpc_resp
            ON rpc_req.message_id = rpc_resp.message_id
            AND rpc_resp.direction = 'Response'
        WHERE rpc_req.method = 'initialize'
            AND rpc_req.direction = 'Request'
            AND rpc_resp.result IS NOT NULL
        """
    ) as cursor:
        rows = await cursor.fetchall()
        for pid, result_str in rows:
            if result_str:
                try:
                    result = json.loads(result_str)
                    server_info = result.get('serverInfo', {})
                    server_name = server_info.get('name', 'Unknown')
                    if pid:
                        pid_to_server[pid] = server_name
                except json.JSONDecodeError:
                    pass


    # Request 통계
    async with db.conn.execute(
        """
        SELECT method, COUNT(*) as count
        FROM rpc_events
        WHERE direction = 'Request' AND method IS NOT NULL
        GROUP BY method
        ORDER BY count DESC
        LIMIT 10
        """
    ) as cursor:
        request_rows = await cursor.fetchall()

    if request_rows:
        for method, count in request_rows:
            print(f"\n📤 {method} ({count:,} requests)")

            # 해당 메서드의 모든 고유한 Response들 가져오기 (Request의 PID 사용)
            # GROUP BY로 크기별로 하나씩만 가져오기
            async with db.conn.execute(
                """
                SELECT r_resp.result, r_req.params, raw_req.pid
                FROM rpc_events r_req
                LEFT JOIN raw_events raw_req
                    ON r_req.raw_event_id = raw_req.id
                LEFT JOIN rpc_events r_resp
                    ON r_req.message_id = r_resp.message_id
                    AND r_resp.direction = 'Response'
                WHERE r_req.method = ? AND r_req.direction = 'Request'
                    AND r_resp.result IS NOT NULL
                GROUP BY LENGTH(r_resp.result)
                ORDER BY LENGTH(r_resp.result) DESC
                LIMIT 10
                """,
                (method,)
            ) as detail_cursor:
                details = await detail_cursor.fetchall()

                if details:
                    for idx, detail in enumerate(details):
                        result_json = json.loads(detail[0]) if detail[0] else None
                        params_json = json.loads(detail[1]) if detail[1] else {}
                        pid = detail[2]

                        # 첫 번째 응답만 params 출력
                        if idx == 0 and params_json:
                            params_str = json.dumps(params_json, ensure_ascii=False)
                            print(f"  └─ Params: {params_str[:80]}")

                        # result 분석
                        if result_json:
                            prefix = "  └─" if idx == 0 else "  ├─"

                            # PID로 서버 이름 매핑 (없으면 응답 내 serverInfo 확인)
                            server_name = pid_to_server.get(pid, "Unknown")
                            if server_name == "Unknown" and 'serverInfo' in result_json:
                                server_name = result_json['serverInfo'].get('name', 'Unknown')

                            # 툴 이름 패턴으로 서버 추론
                            if server_name == "Unknown" and 'tools' in result_json:
                                tools = result_json['tools']
                                if tools:
                                    first_tool = tools[0].get('name', '')
                                    if 'get_alerts' in first_tool or 'get_forecast' in first_tool:
                                        server_name = 'weather'
                                    elif 'create_or_update_file' in first_tool or 'search_repositories' in first_tool:
                                        server_name = 'github-mcp-server'
                                    elif 'read_file' in first_tool or 'write_file' in first_tool:
                                        server_name = 'secure-filesystem-server'

                            if 'tools' in result_json:
                                tools = result_json['tools']
                                if tools:
                                    print(f"{prefix} Response [{server_name}]: {len(tools)} tools")
                                    for tool in tools[:5]:
                                        print(f"      • {tool.get('name', 'unknown')}")
                                    if len(tools) > 5:
                                        print(f"      • ... and {len(tools) - 5} more")
                                else:
                                    print(f"{prefix} Response [{server_name}]: No tools available")
                            elif 'resources' in result_json:
                                resources = result_json['resources']
                                if resources:
                                    print(f"{prefix} Response [{server_name}]: {len(resources)} resources")
                                    for resource in resources[:3]:
                                        print(f"      • {resource.get('name', 'unknown')}")
                                else:
                                    print(f"{prefix} Response [{server_name}]: No resources available")
                            elif 'prompts' in result_json:
                                prompts = result_json['prompts']
                                if prompts:
                                    print(f"{prefix} Response [{server_name}]: {len(prompts)} prompts")
                                    for prompt in prompts[:3]:
                                        print(f"      • {prompt.get('name', 'unknown')}")
                                else:
                                    print(f"{prefix} Response [{server_name}]: No prompts available")
                            elif 'content' in result_json:
                                content = result_json['content'][0].get('text', '')[:100]
                                print(f"{prefix} Response: {content}...")
                            elif 'protocolVersion' in result_json:
                                server_info = result_json.get('serverInfo', {})
                                print(f"{prefix} Response: {server_info.get('name', 'unknown')} v{server_info.get('version', '')}")
                            else:
                                print(f"{prefix} Response: {list(result_json.keys())}")
                else:
                    print(f"  └─ No matching response found")
    else:
        print("  (결과 없음)")
    print()

    # 5. 파일 이벤트 조회
    print("📁 파일 작업 통계:")
    print("-" * 80)
    async with db.conn.execute(
        """
        SELECT operation, COUNT(*) as count
        FROM file_events
        GROUP BY operation
        ORDER BY count DESC
        """
    ) as cursor:
        rows = await cursor.fetchall()
        if rows:
            for row in rows:
                operation, count = row
                print(f"  {operation:20s}: {count:,}")
        else:
            print("  (결과 없음)")
    print()

    # 6. 엔진별 탐지 통계
    print("🔍 엔진별 탐지 통계:")
    print("-" * 80)
    async with db.conn.execute(
        """
        SELECT engine_name,
               COUNT(*) as total,
               SUM(CASE WHEN detected = 1 THEN 1 ELSE 0 END) as detected_count
        FROM engine_results
        GROUP BY engine_name
        """
    ) as cursor:
        rows = await cursor.fetchall()
        if rows:
            for row in rows:
                engine_name, total, detected = row
                detection_rate = (detected / total * 100) if total > 0 else 0
                print(f"  {engine_name:20s}: {detected:4d}/{total:4d} ({detection_rate:.1f}%)")
        else:
            print("  (결과 없음)")
    print()

    # 연결 종료
    await db.close()

    print("=" * 80)
    print("조회 완료!")
    print("=" * 80)


if __name__ == "__main__":
    asyncio.run(main())
