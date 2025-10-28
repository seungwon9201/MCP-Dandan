from kafka import KafkaProducer
import json
from typing import Union, List, Optional


class SimpleKafkaProducer:
    """
    Kafka Producer 클래스
    """

    def __init__(self, brokers: List[str], client_id: str = 'my-app'):
        """
        KafkaProducer 초기화

        Args:
            brokers: Kafka 브로커 주소 리스트 (예: ['localhost:9092'])
            client_id: 클라이언트 ID
        """
        self.brokers = brokers
        self.client_id = client_id
        self.producer = None
        self.is_connected = False

    def connect(self):
        """Kafka 서버에 연결"""
        try:
            self.producer = KafkaProducer(
                bootstrap_servers=self.brokers,
                client_id=self.client_id,
                value_serializer=lambda v: json.dumps(v).encode('utf-8') if isinstance(v, dict) else str(v).encode('utf-8'),
                key_serializer=lambda k: k.encode('utf-8') if k else None
            )
            self.is_connected = True
            print('✓ Kafka 서버에 연결되었습니다.')
        except Exception as e:
            print(f'✗ Kafka 연결 실패: {e}')
            raise

    def send(self, topic: str, messages: List[Union[str, dict]]):
        """
        여러 메시지 전송

        Args:
            topic: 토픽 이름
            messages: 전송할 메시지 리스트
        """
        if not self.is_connected or self.producer is None:
            raise Exception('Kafka에 연결되지 않았습니다. connect()를 먼저 호출하세요.')

        try:
            for message in messages:
                future = self.producer.send(topic, value=message)
                # 전송 완료 대기 (선택사항)
                record_metadata = future.get(timeout=10)

            # 모든 메시지가 전송될 때까지 대기
            self.producer.flush()
            print(f'✓ {len(messages)}개 메시지 전송 성공')

        except Exception as e:
            print(f'✗ 메시지 전송 실패: {e}')
            raise

    def send_one(self, topic: str, message: Union[str, dict], key: Optional[str] = None):
        """
        단일 메시지 전송

        Args:
            topic: 토픽 이름
            message: 전송할 메시지
            key: 메시지 키 (선택사항, 파티셔닝에 사용)
        """
        if not self.is_connected or self.producer is None:
            raise Exception('Kafka에 연결되지 않았습니다. connect()를 먼저 호출하세요.')

        try:
            future = self.producer.send(topic, value=message, key=key)
            record_metadata = future.get(timeout=10)

            print(f'✓ 메시지 전송 성공 - Topic: {record_metadata.topic}, '
                  f'Partition: {record_metadata.partition}, Offset: {record_metadata.offset}')

            return record_metadata

        except Exception as e:
            print(f'✗ 메시지 전송 실패: {e}')
            raise

    def disconnect(self):
        """연결 종료"""
        try:
            if self.producer:
                self.producer.close()
                self.is_connected = False
                print('✓ Kafka 연결이 종료되었습니다.')
        except Exception as e:
            print(f'✗ 연결 종료 실패: {e}')
            raise
