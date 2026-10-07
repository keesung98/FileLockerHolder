# File Lock Holder

지정한 파일을 **원하는 시간 동안 원하는 공유 모드로 열어 둔 채 잡고 있는** Windows용 WPF 테스트 도구입니다.

다른 프로세스가 파일을 읽고 있는 동안 로그 쓰기가 실패하는 현상(예: `IOException: The process cannot access the file because it is being used by another process`)을 재현하고, 쓰는 쪽 프로그램이 이런 상황을 제대로 처리하는지(재시도, 예외 처리 등) 확인하는 용도로 만들었습니다.

## 주요 기능

- **파일 지정**
  - 경로 직접 입력, `찾아보기...` 대화상자, 드래그 앤 드롭 지원
  - `현재 시간대 MES 로그` 버튼: 현재 시각 기준 MES 로그 경로를 자동으로 채움
    `D:\APInovationLog\MES\LOG\yyyy\MM\dd\yyyyMMddHH.txt`
  - 파일 크기, 수정 시각, 속성을 바로 표시
- **잠금 설정**
  - 유지 시간(초, 소수 가능) 입력 및 프리셋(0.5 / 1 / 3 / 5 / 10초)
  - `수동 해제까지 계속 유지` 옵션
  - 공유 모드 선택:

    | 모드 | 동작 | 다른 프로세스 |
    |---|---|---|
    | `FileShare.Read` (기본) | `File.ReadAllLines` 등 일반 읽기와 같은 방식 | 읽기 가능, **쓰기 차단** |
    | `FileShare.None` | 완전 독점 | **읽기·쓰기 모두 차단** |
    | `FileShare.ReadWrite` | 비교용 | 차단 안 됨 |

- **실행 / 상태**
  - `▶ 잠금 시작` / `■ 즉시 해제`
  - 상태 LED, 경과 시간(ms 단위), 남은 시간, 진행률 표시
  - 유지 시간이 끝나면 자동 해제, 프로그램 종료 시에도 자동 해제
- **쓰기 열기 테스트**
  - 로그를 쓰는 쪽과 같은 방식(`FileMode.Append`, `FileAccess.Write`, `FileShare.ReadWrite`)으로 파일을 열었다가 바로 닫음
  - **내용은 쓰지 않으며**, 지금 쓰기가 가능한 상태인지만 확인
- **이벤트 로그**
  - 잠금/해제/실패 기록을 `HH:mm:ss.fff` 시각과 함께 표시 (예외 타입과 HResult 포함)
  - 로그 복사(클립보드), 로그 지우기

## 동작 방식

잠금은 파일을 아래와 같이 열어 둔 상태로 유지하는 방식입니다.

```csharp
new FileStream(path, FileMode.Open, FileAccess.Read, share);
```

- `FileMode.Open`을 사용하므로 **이미 있는 파일만** 대상입니다. 파일을 새로 만들지 않습니다.
- 읽기 전용으로 열기 때문에 **파일 내용은 변경되지 않습니다.**
- 해제 시 스트림을 `Dispose`하여 핸들을 반환합니다.

## 요구 사항

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (빌드 시) 또는 .NET 8 Desktop Runtime (실행 시)

## 빌드 및 실행

```powershell
# 실행
dotnet run

# 릴리스 빌드
dotnet build -c Release

# 단일 실행 파일로 배포 (런타임 미포함)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Visual Studio 2022에서 `FileLockHolder.csproj`를 열어 실행해도 됩니다.

## 사용 예시

1. 테스트 대상 프로그램이 로그를 쓰고 있는 파일을 지정합니다.
2. 공유 모드를 `FileShare.Read`로 두고 유지 시간을 정한 뒤 `▶ 잠금 시작`을 누릅니다.
3. 잠금이 유지되는 동안 대상 프로그램의 로그 쓰기가 어떻게 동작하는지 확인합니다.
   (`쓰기 열기 테스트` 버튼으로 이 도구 안에서 바로 쓰기 실패를 확인할 수도 있습니다.)
4. 시간이 끝나거나 `■ 즉시 해제`를 누르면 잠금이 풀립니다. 이벤트 로그에 실제 유지 시간이 기록됩니다.

## 프로젝트 구조

```
FileLockHolder/
├── App.xaml / App.xaml.cs           # 애플리케이션 진입점, 공통 스타일
├── MainWindow.xaml                  # UI 레이아웃
├── MainWindow.xaml.cs               # 파일 잠금/해제, 타이머, 로그 처리
└── FileLockHolder.csproj            # .NET 8 WPF 프로젝트
```

## 참고

- MES 로그 기본 경로는 `MainWindow.xaml.cs`의 `MesLogRoot` 상수(`D:\APInovationLog\MES\LOG`)에 고정되어 있습니다. 환경에 맞게 바꿔서 사용하세요.
- 운영 중인 시스템의 파일을 잠그면 실제로 쓰기가 실패할 수 있습니다. 테스트 환경에서 사용하는 것을 권장합니다.
