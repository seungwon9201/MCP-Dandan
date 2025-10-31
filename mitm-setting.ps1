<#
install-mitm-ca.ps1
- 관리자 권한으로 실행하세요 (UAC 필요)
- 기능:
  1) mitmdump 존재 여부 확인
  2) 없으면 python -m pip로 mitmproxy 설치 시도
  3) mitmdump를 잠깐 실행하여 %USERPROFILE%\.mitmproxy 안에 CA 파일 생성
  4) 생성된 mitmproxy CA(cert) 파일을 CurrentUser/LocalMachine 신뢰 루트에 설치
* 개선 사항: 설치 후 mitmdump가 PATH에 없으면, Python Scripts 경로를 찾아 PATH에 임시 추가하여 해결
#>

# 관리자 권한 검사
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "관리자 권한이 필요합니다. 관리자 PowerShell로 실행하세요."
    exit 1
}

# ----- 설정 -----
$ConfDir = Join-Path $env:USERPROFILE ".mitmproxy"
$CACertNames = @("mitmproxy-ca-cert.cer","mitmproxy-ca.pem","mitmproxy-ca.p12")   # 찾을 후보 이름들
$FoundCACert = $null
$PyScriptsPath = $null # 파이썬 Scripts 경로 저장용 변수

Write-Host "[*] mitm proxy confdir: $ConfDir"

# 유틸: mitmdump 명령이 있는지 검사
function Test-MitmdumpExists {
    # 환경 변수 PATH에 임시로 추가된 경로도 인식하도록 $env:path를 수동으로 지정하지 않음
    $cmd = Get-Command mitmdump -ErrorAction SilentlyContinue
    return $cmd -ne $null
}

# 유틸: 파이썬 실행기 탐색
function Find-Python {
    $candidates = @("python","python3","py")
    foreach ($p in $candidates) {
        $cmd = Get-Command $p -ErrorAction SilentlyContinue
        if ($cmd) { return $p }
    }
    return $null
}

# 1) mitmdump가 이미 있는지 체크
if (Test-MitmdumpExists) {
    Write-Host "[OK] mitmdump 가 이미 PATH에 있습니다."
} else {
    Write-Warning "mitmdump(또는 mitmproxy)가 PATH에 없습니다. pip로 설치를 시도합니다."

    $py = Find-Python
    if (-not $py) {
        Write-Error "파이썬 실행기(python/python3/py)를 찾을 수 없습니다. 먼저 Python을 설치하고 'Add Python to PATH' 옵션을 켜세요."
        Write-Host "권장: https://www.python.org/downloads/"
        exit 1
    }

    Write-Host "[*] 파이썬 실행기 발견: $py"
    Write-Host "[*] mitmproxy 설치: $py -m pip install mitmproxy"
    try {
        # 'pip' 대신 '$py -m pip' 사용으로 환경 독립성 강화
        & $py -m pip install mitmproxy 2>&1 | ForEach-Object { Write-Host $_ }
    } catch {
        Write-Error "pip로 설치 중 오류 발생: $_"
        exit 1
    }

    # 설치 후 mitmdump가 PATH에 잡히는지 다시 확인
    if (Test-MitmdumpExists) {
        Write-Host "[OK] mitmdump 설치/탐지 성공."
    } else {
        Write-Host "[*] 설치는 되었으나 mitmdump 명령을 바로 찾을 수 없습니다. Scripts 경로를 확인합니다."
        # python scripts 경로 가져오기 시도
        try {
            # 'sysconfig.get_path('scripts')' 사용으로 Scripts 경로 가져오기
            $PyScriptsPath = (& $py -c "import sysconfig; print(sysconfig.get_path('scripts'))").Trim()
        } catch {
            Write-Warning "파이썬 scripts 경로를 가져오는 데 실패했습니다."
        }

        if ($PyScriptsPath -and (Test-Path $PyScriptsPath)) {
            $candidateExe = Join-Path $PyScriptsPath "mitmdump.exe"
            if (Test-Path $candidateExe) {
                Write-Host "[OK] mitmdump 실행파일을 발견했습니다: $candidateExe"
                Write-Host "[*] 현재 PowerShell 세션의 PATH에 '$PyScriptsPath' 를 **임시**로 추가합니다."
                
                # *** 핵심 개선: Scripts 경로를 현재 세션 PATH에 임시 추가하여 해결 ***
                $env:Path = "$PyScriptsPath;$env:Path"
                
                if (Test-MitmdumpExists) {
                     Write-Host "[OK] PATH 임시 추가 후 mitmdump 명령 인식 성공."
                } else {
                     Write-Warning "PATH 임시 추가 후에도 mitmdump 명령을 인식하지 못했습니다."
                }
            } else {
                Write-Error "mitmdump가 설치된 것 같으나 실행파일 ($candidateExe)을 찾을 수 없습니다."
                Write-Host "수동 확인: $py -m pip show mitmproxy"
                exit 1
            }
        } else {
            Write-Error "파이썬 scripts 경로를 가져올 수 없거나 경로가 존재하지 않습니다."
            Write-Host "mitmproxy가 제대로 설치되었는지 수동으로 확인하세요: $py -m pip show mitmproxy"
            exit 1
        }
    }
}

# 2) mitm CA 파일이 이미 있는지 확인
foreach ($name in $CACertNames) {
    $path = Join-Path $ConfDir $name
    if (Test-Path $path) {
        $FoundCACert = $path
        break
    }
}

# 3) CA 파일이 없으면 mitmdump를 잠깐 실행하여 CA 생성 시도
if (-not $FoundCACert) {
    Write-Host "[*] mitm proxy CA 파일을 찾을 수 없습니다. mitmdump를 잠깐 실행하여 CA를 생성합니다..."
    # confdir가 없으면 생성
    if (-not (Test-Path $ConfDir)) {
        try { New-Item -ItemType Directory -Path $ConfDir -Force | Out-Null } catch {}
    }

    # mitmdump 실행 (백그라운드). confdir 강제 지정
    $mitmArgs = @("--set", "confdir=$ConfDir")
    # 추가: --set console_eventlog_verbosity=error 로 로그 과다 출력 억제 (mitmproxy 옵션이 버전에 따라 다를 수 있음)
    try {
        # PATH에 임시 추가했으므로 'mitmdump' 직접 실행 가능
        $proc = Start-Process -FilePath "mitmdump" -ArgumentList $mitmArgs -WindowStyle Hidden -PassThru -ErrorAction Stop
    } catch {
        Write-Warning "mitmdump 실행 실패: $_"
        Write-Host "수동으로 mitmdump를 실행한 뒤 http://mitm.it 에 접속하거나 '%USERPROFILE%\.mitmproxy' 폴더를 확인하세요."
        exit 1
    }

    # 최대 대기: 15초 (1초 간격으로 확인)
    $maxWait = 15
    $waited = 0
    while ($waited -lt $maxWait -and -not $FoundCACert) {
        Start-Sleep -Seconds 1
        $waited += 1
        foreach ($name in $CACertNames) {
            $path = Join-Path $ConfDir $name
            if (Test-Path $path) {
                $FoundCACert = $path
                break
            }
        }
        if ($FoundCACert) { break }
    }

    # 프로세스가 남아있다면 종료 (우리가 잠깐 실행한 것이므로 종료)
    try {
        if ($proc -and -not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
    } catch {}

    if (-not $FoundCACert) {
        Write-Warning "자동으로 CA 파일을 생성하지 못했습니다. 아래 방법 중 하나를 시도하세요:"
        Write-Host " 1) 관리자 PowerShell에서 'mitmdump --set confdir=$ConfDir' 를 직접 실행한 뒤 브라우저에서 http://mitm.it 접속"
        Write-Host " 2) mitmdump를 실행한 상태에서 브라우저(또는 대상 클라이언트)로 http://mitm.it 접속하여 플랫폼별 cert를 내려받아 설치"
        Write-Host " 3) '%USERPROFILE%\.mitmproxy' 폴더를 확인하여 mitmproxy-ca-cert.cer 또는 mitmproxy-ca.pem 파일을 수동으로 찾아 설치"
        exit 1
    } else {
        Write-Host "[OK] 생성된 CA 파일 발견: $FoundCACert"
    }
} else {
    Write-Host "[OK] 이미 존재하는 CA 파일 발견: $FoundCACert"
}

# 4) CA를 신뢰 루트 저장소에 설치 (CurrentUser 및 LocalMachine Root)
Write-Host "[*] CA 인증서를 신뢰 루트 저장소에 설치 중..."
try {
    # CurrentUser Root
    Import-Certificate -FilePath $FoundCACert -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    # LocalMachine Root (관리자 권한에서만 가능)
    Import-Certificate -FilePath $FoundCACert -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Write-Host "[OK] 인증서가 CurrentUser 및 LocalMachine 신뢰 루트에 설치되었습니다."
} catch {
    Write-Warning "Import-Certificate 중 오류 발생: $_"
    Write-Host "대안: certutil로 수동 설치 시도"
    try {
        & certutil -addstore Root $FoundCACert | Out-Null
        Write-Host "[OK] certutil로 루트 저장소에 추가 시도 완료."
    } catch {
        Write-Error "certutil 설치 시도도 실패했습니다: $_"
        Write-Host "수동으로 '%USERPROFILE%\.mitmproxy\$([IO.Path]::GetFileName($FoundCACert))' 파일을 찾아 설치하세요."
        exit 1
    }
}

Write-Host ""
Write-Host "=== 완료 ==="
Write-Host "생성된 CA 파일: $FoundCACert"
Write-Host "mitmdump(설치여부): " -NoNewline
if (Test-MitmdumpExists) { Write-Host "있음 (현재 세션에서 사용 가능)" } else { Write-Host "없음 (설치/경로 확인 필요)" }
if ($PyScriptsPath) { Write-Host "참고: Scripts 경로 '$PyScriptsPath' 가 현재 세션 PATH에 임시 추가되었습니다." }
Write-Host ""
Write-Host "인증서 제거 시 (관리자):"
Write-Host "certutil -delstore Root `"$((Get-Item $FoundCACert).BaseName.Replace('mitmproxy-ca-cert','mitmproxy'))`""
Write-Host "또는 인증서 관리 MMC(certmgr.msc 또는 certlm.msc)에서 'Trusted Root Certification Authorities' 에서 수동 삭제 가능."