# 82ch-web - MCP Chat Application

MCP (Model Context Protocol) 서버들을 관리하고 모니터링할 수 있는 웹 애플리케이션입니다.

## 프로젝트 구조

```
82ch-web/
├── front/          # React + Vite 프론트엔드
├── back/           # Express 백엔드 API
└── README.md
```

## 주요 기능

### 1. MCP 서버 관리
- 여러 MCP 서버를 한눈에 관리
- 서버별 도구(tool) 목록 및 설명 확인
- 실시간 서버 상태 모니터링

### 2. 채팅 인터페이스
- tool/call 및 tool/response 메시지 표시
- 말풍선 형태의 직관적인 UI
- 메시지 선택 시 상세 정보 표시

### 3. 반응형 레이아웃
- 3단 분할 레이아웃 (사이드바, 중앙 패널, 채팅 패널)
- 드래그로 패널 크기 조절 가능
- 사이드바 접기/펼치기 기능

### 4. 보안 분석
- 메시지별 악성 점수 표시
- 파라미터 분석 및 시각화
- 실시간 위협 탐지 결과 표시

## 기술 스택

### 프론트엔드
- **React 18** - UI 라이브러리
- **Vite** - 빌드 도구
- **Tailwind CSS** - 스타일링
- **Lucide React** - 아이콘

### 백엔드
- **Node.js** - 런타임
- **Express 5** - 웹 프레임워크
- **CORS** - Cross-Origin 요청 처리

## 설치 및 실행

### 1. 프로젝트 클론

```bash
git clone <repository-url>
cd 82ch-web
```

### 2. 백엔드 설치 및 실행

```bash
cd back
npm install
npm start
```

백엔드 서버가 http://localhost:3001 에서 실행됩니다.

### 3. 프론트엔드 설치 및 실행

```bash
cd front
npm install
npm run dev
```

프론트엔드 개발 서버가 http://localhost:5173 에서 실행됩니다.

### 4. 브라우저에서 접속

http://localhost:5173 으로 접속하여 애플리케이션을 사용합니다.

## API 엔드포인트

### GET /api/servers
모든 MCP 서버 목록을 반환합니다.

**응답 예시:**
```json
[
  {
    "id": 1,
    "name": "filesystem",
    "icon": "📁",
    "type": "File System Server",
    "tools": [...]
  }
]
```

### GET /api/servers/:id
특정 서버의 정보를 반환합니다.

### GET /api/servers/:id/messages
특정 서버의 메시지 목록을 반환합니다.

**응답 예시:**
```json
[
  {
    "id": 1,
    "type": "tool_call",
    "timestamp": "2/16",
    "data": {
      "tool": "read_file",
      "params": { "path": "/home/user/document.txt" }
    },
    "maliciousScore": 0
  }
]
```

### GET /api/messages
모든 서버의 메시지를 반환합니다.

## 지원하는 MCP 서버

1. **filesystem** - 파일 시스템 관리
   - read_file, read_text_file, read_media_file, file_search

2. **Weather** - 날씨 정보 API
   - get_current_weather, get_forecast, get_alerts

3. **NOTION** - Notion 통합
   - create_page, update_page, search_pages, get_database

4. **Gmail** - Gmail 통합
   - send_email, read_emails, search_emails

5. **malicious** - 보안 분석
   - scan_file, check_url, analyze_behavior

## 주요 컴포넌트

### 프론트엔드

- **App.jsx** - 메인 애플리케이션 컴포넌트, 레이아웃 및 상태 관리
- **LeftSidebar.jsx** - MCP 서버 목록 사이드바
- **MiddleTopPanel.jsx** - 서버 정보 및 도구 목록 표시
- **MiddleBottomPanel.jsx** - 선택된 메시지의 상세 정보 표시
- **RightChatPanel.jsx** - 채팅 메시지 표시 (말풍선 형태)

### 백엔드

- **index.js** - Express 서버 및 API 라우트 정의

## 사용 방법

1. **서버 선택**: 왼쪽 사이드바에서 MCP 서버를 선택합니다.
2. **도구 확인**: 중앙 상단 패널에서 선택한 서버의 도구 목록을 확인합니다.
3. **메시지 확인**: 오른쪽 채팅 패널에서 tool/call 및 tool/response 메시지를 확인합니다.
4. **상세 정보**: 메시지를 클릭하면 중앙 하단 패널에 파라미터 및 분석 결과가 표시됩니다.
5. **크기 조절**: 패널 경계선을 드래그하여 원하는 크기로 조절합니다.

## 개발

### 프론트엔드 개발 모드

```bash
cd front
npm run dev
```

### 백엔드 개발 모드

```bash
cd back
npm run dev
```

### 빌드

```bash
cd front
npm run build
```

## 라이선스

ISC

## 기여

이슈 및 풀 리퀘스트를 환영합니다!
