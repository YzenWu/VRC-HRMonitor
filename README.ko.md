# HeartRateMonitor

VRChat를 위한 실시간 BLE 심박 도구: OSC로 심박수와 하드웨어 텔레메트리를 채팅박스에 푸시합니다 — 플로팅 창, 원격 웹 프론트엔드, CLI/TUI, VRChat 툴킷을 하나의 Windows 앱에 담았습니다.

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [繁體中文（香港）](README.zh-HK.md) | [粵語（香港）](README.yue-HK.md) | [日本語](README.ja.md) | [Español](README.es.md) | **한국어** | [Deutsch](README.de.md) | [Français](README.fr.md)

<!-- BADGES PLACEHOLDER: 여기에 release / license / platform 배지(shields.io)를 삽입 -->

![메인 화면 개요](docs/images/hero.png)
<!-- IMAGE PLACEHOLDER: 메인 윈도우 개요 — 사이드바, 심박 페이지, 하단 바의 기기 필 -->

## 기능

### BLE 심박 기기

- 표준 Bluetooth Low Energy 심박 기기(Heart Rate Service `0x180D`)를 모두 지원 — 가슴 스트랩, 스마트 밴드, 스포츠 워치.
- **다중 기기 지원**: 여러 센서를 동시에 연결할 수 있으며, 한도는 블루투스 스택과 하드웨어가 정합니다.
- 스마트 기기 점수 산정·정렬, 별명, 자동 재연결, 약신호(RSSI) 경고, 기기별 플로팅 창.
- 자동 감지: 가중치에 따라 후보 기기를 일괄 연결하며, 오디오/스마트홈 기기와 심박 특성이 없는 기기는 자동으로 건너뜁니다.

![심박 곡선](docs/images/heartbeat.png)
<!-- IMAGE PLACEHOLDER: 심박 페이지 — 큰 BPM 표시, 실시간 곡선(메인/평균/기기별), 기기 목록 -->

### VRChat OSC 채팅박스 푸시 및 실시간 미리보기

- 자유로운 `{변수}` 템플릿으로 「심박수 + CPU / GPU / RAM 등」을 OSC/UDP를 통해 VRChat 채팅박스(`/chatbox/input`)로 푸시합니다.
- 편집 중 매초 갱신되는 템플릿 실시간 미리보기와 글자 수 카운터, 채팅박스 144자 제한에 가까워지면 차단 없이 경고만 표시합니다.
- 사용자 지정 OSC 전송(임의 주소/텍스트), Webhook 아웃바운드 푸시, OSC 수신(9001 포트)으로 VRChat 아바타 파라미터 트래픽 캡처, 「시작 시 자동 푸시」 옵션.

![푸시 미리보기](docs/images/pusher.png)
<!-- IMAGE PLACEHOLDER: 푸시 페이지 — 실시간 미리보기와 글자 수 카운터가 있는 OSC 템플릿 편집기 -->

### 플로팅 창

- 현재 BPM(또는 이미지)을 표시하는 항상 위 데스크톱 위젯; 메인 창 1개 + 기기별 창.
- 클릭 투과 가능한 잠금, DPI 인식 크기 조절, 창별 위치·크기 독립 저장.
- 창마다 데이터 소스(평균 또는 특정 기기)와 갱신 간격을 설정할 수 있습니다.

![플로팅 창](docs/images/float-window.png)
<!-- IMAGE PLACEHOLDER: 게임/데스크톱 위에 떠 있는 플로팅 창 — 메인 창과 기기별 창 -->

### 하드웨어 텔레메트리 변수

- 레지스트리, WMI, PowerShell, `systeminfo`로 Windows 호스트 정보를 수집하고, 실시간 지표(CPU/RAM/GPU/VRAM 사용률, 온도, 디스크, 커밋 메모리)는 작업 관리자와 같은 원천인 PDH에서 가져옵니다.
- 모든 것이 템플릿 변수가 됩니다: `{CPU_USAGE}`, `{RAM_PERCENT}`, `{TIME_ISO}`, NTP 동기화 시각… 여기에 사용자 지정 변수(사칙연산/연결/정규식/명령 출력), 변수별 이름 변경/덮어쓰기/단위까지.
- `CPU_USAGE_VRCHAT`, `MEM_USAGE_<이름|PID>`, `USAGE_FILE_<경로>` 같은 동적 프로세스 변수.

### 건강 상태

- 안정 심박 보정과 임계값 계수로 상태(수면 / 안정 / 활동 / 흥분)를 도출하고, OSC 포즈 파라미터(AFK / 착석 / 이동 속도)에도 반응합니다.
- `{HEALTH_STATUS}` 변수로 노출되어 푸시 템플릿에 바로 사용할 수 있습니다.

### 기록 및 내보내기

- 심박, OSC 트래픽, 건강 상태, 하드웨어 스냅샷을 로컬 SQLite에 기록하며, 일 단위 JSONL/CSV 백엔드를 선택할 수 있습니다.
- 다섯 가지 기록 범주(아바타 변경, VRChat 세션, 기기 연결, 심박 상세, 하드웨어 스냅샷) 각각 보존 기간을 따로 설정합니다.
- 통계 페이지(최소/평균/중앙값/최대/표준편차, 추세, 히스토그램, 기기별, OSC 주소 Top-N)는 구간 선택을 지원하며 TXT/JSON/YAML/CSV로 내보낼 수 있습니다.

### 원격 웹 세컨드 프론트엔드(LAN/WAN 등급 + HTTPS)

- 동일한 UI를 동일한 단일 포트(기본 9460)로 네트워크의 휴대폰과 태블릿에 제공합니다.
- 접속 원천 기반 등급 접근: 루프백 연결은 로컬 관리자(로그인 불필요), LAN 원천은 Remote 스위치와 로컬 계정 필요, 공개/WAN 원천은 추가로 WAN 스위치가 필요하며 — 이는 관리자 강력 비밀번호와 명시적인 위험 확인 대화상자를 요구합니다.
- 역할(admin/user)별 섹션 허용 목록, PBKDF2 비밀번호 저장, UA 묶임 세션과 유휴 만료, 감사 로그, 인증서 지문 기반 선택적 HTTPS.

![원격 휴대폰 화면](docs/images/remote-mobile.png)
<!-- IMAGE PLACEHOLDER: 휴대폰에서 연 원격 웹 프론트엔드 — 모바일 레이아웃의 심박 페이지 -->

### CLI / TUI

- `hrmcli.exe`(`HeartRateMonitor.exe --cli`와 완전히 동일): 스크립트용 원샷 명령, 일반 텍스트 REPL(`--shell`), 기본값은 TestDisk 스타일 메뉴 TUI.
- 기기, OSC, 푸시 템플릿, 하드웨어 변수, 건강, 기록/내보내기, 웹/원격, UI 설정, 플로팅 창, 로그를 아우르는 약 40개 명령 — 앱 내 콘솔 탭과 동일한 명령 엔진 사용.

### Toolkit

사이드바 왼쪽 하단의 독에서 VRChat 툴킷을 엽니다:

- **config 편집기** — VRChat `config.json`의 주요 필드를 표 형태로 편집, 엄격한 JSON 타입 검증 포함.
- **로그 브라우저** — VRChat 로그의 목록/읽기/검색.
- **캐시 정리** — dry-run 미리보기와 확인 문구가 있는 캐시 사용량 분석 및 정리.
- **사진 색인** — 사진 라이브러리 병렬 색인화와 키워드 검색(VRChat 스크린샷 XMP 메타데이터).
- **게임 통계** — 플레이 시간/심박/하드웨어 데이터의 집계 통계와 차트.
- **프로세스 분석** — VRChat 프로세스의 CPU/메모리 스냅샷.

![Toolkit](docs/images/toolkit.png)
<!-- IMAGE PLACEHOLDER: Toolkit 페이지 — 펼쳐진 독 메뉴와 캐시 분석 도구 -->

### 안전 모드

- `--safemode`(또는 설정 화면 진입 / R 세 번 입력)로 자동 연결, 자동 감지, 자동 재연결, OSC 푸시, 하드웨어 수집 등 모든 자동화를 일시 중지해 문제 해결을 돕습니다. 활성 중에는 상시 배너가 표시되며 한 번의 클릭으로 정상 재시작할 수 있습니다.

### UI: 10개 언어와 테마

- 인터페이스는 10개 언어 지원: 繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français; CLI와 로그도 같은 언어를 따릅니다.
- 라이트/다크 모드 × 색상 팔레트(기본 / 숲 / 노을 / 바다 / 보라 / 강조·배경·패널 색을 직접 고르는 **단색 사용자 지정 모드**), 모서리 둥글기와 밀도 슬라이더, 시스템 테마 따라가기, 전역 애니메이션 스위치.
- 레이아웃 기본 설정(카드 순서, 열 너비, 곡선 설정…)은 로컬에 저장될 뿐 아니라 백엔드로 미러링되어 재설치 후에도 유지됩니다.

## 시스템 요구 사항

- Windows 10 또는 11, 64비트(x64).
- Microsoft Edge WebView2 런타임(대부분 시스템에 사전 설치됨, 없다면 Microsoft Evergreen 런타임 설치).
- BLE를 지원하는 블루투스 어댑터(내장형 또는 USB 동글).
- 선택: 프레임워크 의존 빌드에는 .NET 10 런타임이 필요 — standalone 빌드는 자체 포함입니다.

## 빌드된 ZIP으로 사용하기

1. [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases)에서 최신 `HeartRateMonitor-*-x64.zip`을 내려받아 아무 곳에나 압축 해제합니다.
2. `HeartRateMonitor.exe`(또는 `hrm-webui.exe`)를 실행하면 엔진이 시스템 트레이로 들어가고 스플래시 화면과 함께 WebView2 창이 열립니다.
3. 첫 실행 시 설정은 데이터 디렉터리 `%AppData%\HeartRateMonitor`에 기록됩니다(로그, 내보내기, 데이터베이스도 같은 곳에 있으며, 실행 파일 옆의 `data_location.txt`로 위치를 변경할 수 있습니다).
4. 스캔을 시작하고 BLE 센서를 연결한 뒤 OSC 푸시를 켜고 VRChat에 접속하면 채팅박스가 갱신되기 시작합니다.
5. `hrmcli.exe`는 터미널 버전이며, `hrmdump.exe`는 크래시 감시자로 자동 실행됩니다.
6. 휴대폰에서 원격 접속: Remote 스위치(Web 탭)를 켜고, 같은 네트워크의 휴대폰에서 `http://<PC-IP>:9460/webui/`를 열어 로컬 계정으로 로그인하면 됩니다.

## 소스에서 빌드하기

> 이 저장소는 프로젝트의 소스 스냅샷입니다.

사전 요구 사항:

- **gcc(MinGW-w64)** — C OSC 엔진(`Engine/`) 컴파일.
- **.NET 10 SDK** — 네 개의 C# 실행 파일(`HeartRateMonitor.exe`, `hrm-webui.exe`, `hrmcli.exe`, `hrmdump.exe`) 게시.
- **Node.js + npm** — Vue 3 프론트엔드(`WebUI/`) 빌드.

저장소 루트에서 빌드(PowerShell):

```powershell
./build.ps1                # 대화형 선택: standalone / releases / debug
./build.ps1 --standalone   # 자체 포함 단일 파일 실행 파일
./build.ps1 --releases     # 프레임워크 의존 릴리스 + ZIP
./build.ps1 --debug        # 디버그 빌드: 콘솔 + 상세 로그
```

스크립트는 C 엔진, 웹 프론트엔드(vite), 네 개의 .NET 프로젝트를 차례로 빌드해 `Built/<브랜치>-<타임스탬프>/`에 출력합니다. 릴리스 빌드는 SHA-256 사이드카를 동봉한 `HeartRateMonitor-*-x64.zip`을 추가로 만듭니다. `Release.json`은 릴리스 메타데이터(버전, 아이콘, 저장소, 빌드 시각, 라이선스)의 단일 원천이며 빌드 시 실행 파일에 포함됩니다. 두 빌드를 병렬로 실행하지 마세요(중간 `obj/` 디렉터리를 공유합니다).

## 라이선스

[MIT](LICENSE) — © Yzen Wu
