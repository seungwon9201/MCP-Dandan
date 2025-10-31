# 🧩 MCP Proxy 사용법

## 📦 mitmproxy 설치

### Windows
1. **공식 바이너리 다운로드**
   - [mitmproxy 다운로드 페이지](https://mitmproxy.org/)에서 Windows용 설치 파일을 다운로드합니다.
   - 또는 winget 사용:
   ```powershell
   winget install mitmproxy.mitmproxy
   ```

2. **설치 확인**
   ```powershell
   mitmproxy --version
   ```

### macOS
1. **Homebrew로 설치**
   ```bash
   brew install mitmproxy
   ```

2. **설치 확인**
   ```bash
   mitmproxy --version
   ```

### Linux
1. **패키지 매니저로 설치**
   ```bash
   # Ubuntu/Debian
   sudo apt install mitmproxy

   # Arch Linux
   sudo pacman -S mitmproxy

   # Fedora
   sudo dnf install mitmproxy
   ```

2. **pip로 설치 (모든 플랫폼 공통)**
   ```bash
   pip install mitmproxy
   ```

3. **설치 확인**
   ```bash
   mitmproxy --version
   ```

### 인증서 설정
mitmproxy를 처음 실행하면 자동으로 인증서가 생성됩니다. HTTPS 트래픽을 가로채려면 인증서를 시스템에 설치해야 합니다.

1. mitmproxy 실행 후 브라우저에서 `http://mitm.it` 접속
2. 사용 중인 OS에 맞는 인증서 다운로드 및 설치
3. Windows의 경우: 인증서를 "신뢰할 수 있는 루트 인증 기관" 저장소에 설치

## ⚙️ config.json 설정
빌드된 `MCPProxy.exe`의 경로를 `command`에 입력합니다.
```json
{
  "mcpServers": {
    "filesystem": {
      "command": "C:\\Users\\ey896\\OneDrive\\Desktop\\82ch-observer\\src\\MCPProxy\\bin\\Debug\\net9.0\\MCPProxy.exe",
      "args": [
        "C:\\Program Files\\nodejs\\npx.cmd",
        "-y",
        "@modelcontextprotocol/server-filesystem",
        "C:\\"
      ]
    },
    "weather": {
      "command": "C:\\Users\\ey896\\OneDrive\\Desktop\\82ch-observer\\src\\MCPProxy\\bin\\Debug\\net9.0\\MCPProxy.exe",
      "args": [
        "C:\\Users\\ey896\\.local\\bin\\uv.exe",
        "--directory",
        "C:\\Users\\ey896\\Downloads\\quickstart-resources\\weather-server-python",
        "run",
        "weather.py"
      ]
    }
  }
}
```

## ⚡ MCPTrace 실행
1. `MCPTrace.exe`를 **관리자 권한으로 실행**합니다.
2. 모니터링할 프로세스를 선택하면 **ETW 기반 시스템 이벤트**가 수집됩니다.
3. Proxy(`MCPProxy.exe`)와 연결되면 **실시간 JSON-RPC 및 시스템 이벤트 로그**가 콘솔에 표시됩니다.
