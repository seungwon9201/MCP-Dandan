import zmq
import json
from queue import Queue, Full
from threading import Thread, Event


class EventProvider:
    """
    이벤트 제공자 (ZeroMQ Subscriber)

    MCPCollector의 ZeroMQ Publisher로부터 이벤트를 수신하여 메인 큐에 푸시합니다.
    """

    def __init__(self, main_queue: Queue, zmq_address: str = "tcp://localhost:5555"):
        """
        Args:
            main_queue: 메인 이벤트 큐 (이벤트를 푸시할 대상)
            zmq_address: ZeroMQ Publisher 주소 (기본값: tcp://localhost:5555)
        """
        self.main_queue = main_queue
        self.zmq_address = zmq_address

        self._context = None
        self._socket = None
        self._read_thread = None
        self._stop_event = Event()
        self._running = False

    def start(self):
        """ZeroMQ 연결 시작 및 읽기 스레드 시작"""
        if self._running:
            return

        self._stop_event.clear()
        self._running = True

        # ZeroMQ 컨텍스트 및 소켓 초기화
        try:
            self._context = zmq.Context()
            self._socket = self._context.socket(zmq.SUB)
            self._socket.connect(self.zmq_address)
            
            # 모든 토픽 구독 (빈 문자열 = 모든 메시지)
            self._socket.setsockopt_string(zmq.SUBSCRIBE, "")
            
            # 타임아웃 설정 (1초)
            self._socket.setsockopt(zmq.RCVTIMEO, 1000)
            
            print(f'✓ ZeroMQ Subscriber 연결됨: {self.zmq_address}')

        except Exception as e:
            print(f'✗ ZeroMQ 연결 실패: {e}')
            self._running = False
            return

        # 읽기 스레드 시작
        self._read_thread = Thread(target=self._read_loop, daemon=True)
        self._read_thread.start()

    def stop(self):
        """ZeroMQ 연결 및 읽기 스레드 중지"""
        if not self._running:
            return

        self._stop_event.set()

        # 읽기 스레드 종료 대기
        if self._read_thread and self._read_thread.is_alive():
            self._read_thread.join(timeout=2)

        # ZeroMQ 소켓 정리
        if self._socket:
            self._socket.close()
            self._socket = None

        if self._context:
            self._context.term()
            self._context = None

        self._running = False
        print('✓ EventProvider 중지됨')

    def _read_loop(self):
        """
        ZeroMQ 소켓에서 이벤트를 읽어 메인 큐에 푸시

        - JSON 형식 검증
        - eventType 필드 확인
        - 메인 큐가 가득 차면 이벤트 드롭
        """
        while not self._stop_event.is_set():
            try:
                # ZeroMQ 메시지 수신 (multipart: topic + data)
                message = self._socket.recv_multipart()
                
                if len(message) < 2:
                    continue
                
                # topic = message[0].decode('utf-8')  # 현재는 사용하지 않음
                json_data = message[1].decode('utf-8')
                
                # 출력 형식 검증
                is_valid, data = self._validate_output(json_data)

                if is_valid:
                    try:
                        # 메인 큐에 푸시 (큐가 가득 차면 스킵)
                        self.main_queue.put(data, block=False)

                    except Full:
                        print(f'⚠️  메인 큐가 가득 찬, 이벤트 드롭')
                        continue

            except zmq.Again:
                # 타임아웃 (정상 동작)
                continue
            except zmq.ZMQError as e:
                if not self._stop_event.is_set():
                    print(f'✗ ZeroMQ 오류: {e}')
                break
            except Exception as e:
                if not self._stop_event.is_set():
                    print(f'✗ 이벤트 수신 오류: {e}')

    def _validate_output(self, output_line: str):
        """
        이벤트 데이터를 검증하는 함수

        Args:
            output_line: 검사할 JSON 문자열

        Returns:
            tuple: (유효 여부, 파싱된 데이터) - 유효하면 (True, data), 아니면 (False, None)

        검증 로직:
        1. JSON 형식인지 확인
        2. "eventType" 항목이 존재하는지 확인
        """
        try:
            # 1. JSON 형식인지 확인
            data = json.loads(output_line)

            # 2. "eventType" 항목이 존재하는지 확인
            if "eventType" in data:
                return True, data
            else:
                return False, None

        except json.JSONDecodeError:
            # JSON 파싱 실패
            return False, None
        except Exception:
            # 기타 오류
            return False, None