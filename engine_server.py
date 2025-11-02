#!/usr/bin/env python3
"""
82ch-Engine Server

ZeroMQ를 통해 MCPCollector로부터 이벤트를 수신하고,
등록된 엔진들에게 분배하여 분석 결과를 로그로 저장합니다.
"""

from engine.sensitive_file_engine import SensitiveFileEngine
from engine.semantic_gap_engine import SemanticGapEngine
from config_loader import ConfigLoader
from event_provider import EventProvider
from event_distributor import EventDistributor
from log_writer import LogWriter
from queue import Queue
import time
import signal
import sys


# 전역 변수로 모든 컴포넌트 관리
event_provider = None
event_distributor = None
engines = []
log_writer = None


def signal_handler(sig, frame):
    """Ctrl+C 처리"""
    print('\n\n프로그램을 종료합니다...')

    # 모든 컴포넌트 종료
    if event_provider:
        event_provider.stop()

    if event_distributor:
        event_distributor.stop()

    for engine in engines:
        engine.stop()

    if log_writer:
        log_writer.stop()

    sys.exit(0)


def main():
    """통합 분석 엔진 서버"""
    global event_provider, event_distributor, engines, log_writer

    print("=" * 80)
    print("82ch-Engine Server (ZeroMQ Edition)")
    print("=" * 80)

    # 설정 파일 로드
    config = ConfigLoader()

    # 큐 생성
    main_queue = Queue(maxsize=config.get_main_queue_maxsize())
    event_log_queue = Queue(maxsize=config.get_event_log_queue_maxsize())
    result_log_queue = Queue(maxsize=config.get_result_log_queue_maxsize())

    print(f"\n큐 설정:")
    print(f"  - 메인 큐 크기: {config.get_main_queue_maxsize()}")
    print(f"  - 엔진 큐 크기: {config.get_engine_queue_maxsize()}")
    print(f"  - 이벤트 로그 큐 크기: {config.get_event_log_queue_maxsize()}")
    print(f"  - 결과 로그 큐 크기: {config.get_result_log_queue_maxsize()}")

    # 엔진 생성 및 엔진 큐 매핑
    engine_queues = {}

    # Sensitive File Engine
    if config.get_sensitive_file_enabled():
        sensitive_engine_queue = Queue(maxsize=config.get_engine_queue_maxsize())
        sensitive_engine = SensitiveFileEngine(sensitive_engine_queue, result_log_queue)

        engines.append(sensitive_engine)
        engine_queues['SensitiveFileEngine'] = sensitive_engine_queue

    # Semantic Gap Engine
    if config.get_semantic_gap_enabled():
        semantic_gap_queue = Queue(maxsize=config.get_engine_queue_maxsize())
        semantic_gap_engine = SemanticGapEngine(
            semantic_gap_queue,
            result_log_queue,
            detail_mode=False  # True로 설정하면 상세 JSON 결과
        )
        engines.append(semantic_gap_engine)
        engine_queues['SemanticGapEngine'] = semantic_gap_queue

    print(f"\n실행 중인 엔진:")
    for i, engine in enumerate(engines, 1):
        print(f"  {i}. {engine.name}")
        print(f"     • 입력 큐: 전용 큐 (크기: {config.get_engine_queue_maxsize()})")
        print(f"     • 출력: 로그 큐")

    # EventProvider 생성 (ZeroMQ)
    zmq_address = config.get_zmq_address()
    event_provider = EventProvider(main_queue, zmq_address)

    # EventDistributor 생성 (이벤트 로그 큐 추가)
    event_distributor = EventDistributor(main_queue, engine_queues, event_log_queue)

    # LogWriter 생성 (2개 큐)
    log_writer = LogWriter(event_log_queue, result_log_queue)

    try:
        # 모든 컴포넌트 시작
        print("\n컴포넌트 시작 중...")

        # 1. LogWriter 시작
        log_writer.start()

        # 2. 모든 엔진 시작
        for engine in engines:
            engine.start()

        # 3. EventDistributor 시작 (이벤트 분배)
        event_distributor.start()

        # 4. EventProvider 시작 (ZeroMQ 연결)
        event_provider.start()

        print("\n" + "=" * 80)
        print("✓ 모든 컴포넌트가 실행 중입니다.")
        print("  MCPCollector가 실행 중이어야 이벤트를 수신할 수 있습니다.")
        print("  종료하려면 Ctrl+C를 누르세요.")
        print("=" * 80 + "\n")

        # Ctrl+C 핸들러 등록
        signal.signal(signal.SIGINT, signal_handler)

        # 계속 실행
        while True:
            time.sleep(1)

    except Exception as e:
        print(f"\n오류 발생: {e}")

        # 모든 컴포넌트 종료
        if event_provider:
            event_provider.stop()
        if event_distributor:
            event_distributor.stop()
        for engine in engines:
            engine.stop()
        if log_writer:
            log_writer.stop()

        sys.exit(1)


if __name__ == '__main__':
    main()