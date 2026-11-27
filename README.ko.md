<p align="center">
  <img src="docs/images/logo.png" alt="Purin Pointer" width="240">
</p>

<h1 align="center">Purin Pointer</h1>

<p align="center">
  <b>Windows를 위한 애니메이션 커서 스튜디오</b><br>
  이미지, GIF, 커서 파일로 나만의 Windows 마우스 포인터를 만드세요.
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.tw.md">繁體中文</a> · <a href="README.zh-Hans.md">简体中文</a> · <a href="README.ja.md">日本語</a> · <b>한국어</b>
</p>

---

Purin Pointer는 AniOnMac의 Windows 버전입니다. `.ani`, `.cur`, PNG, GIF 또는 이미지 폴더를 가져와 클릭 한 번으로 배경을 지우고, 핫스팟을 정한 뒤 Windows 포인터로 적용할 수 있습니다. 그림 실력도, 관리자 권한도 필요 없습니다.

![Purin Pointer 메인 창](docs/images/ko/main.png)

## 기능

- **테마**: 원하는 만큼 만들 수 있으며, 사이드바에서 추가, 복제, 삭제할 수 있습니다. 적용 중인 테마에는 *사용 중* 표시가 붙고, 모든 내용은 자동으로 저장됩니다.
- **4가지 모드와 13가지 포인터**: 기본 포인터, 링크 선택, 텍스트 선택, 대기 상태에 더해 *백그라운드 작업*을 포함한 나머지 13가지 Windows 포인터를 *다른 포인터*에서 설정할 수 있습니다(*백그라운드 작업*은 따로 설정하기 전까지 대기 상태 커서를 따릅니다). 채워진 모드만 적용됩니다.
- **다양한 소스**: `.ani`, `.cur`, `.ico`, PNG, JPG, BMP, 애니메이션 GIF 또는 번호가 붙은 이미지 폴더.
- **타임라인**: 프레임 순서를 바꾸거나 삭제하고, 프레임 시간을 설정하며 실시간으로 미리 볼 수 있습니다.
- **배경 제거**: 단색 배경에는 *자동 제거*, 모든 프레임에서 한 가지 색을 지울 때는 *마술봉*을 사용합니다.
- **핫스팟**: 확대된 미리보기에서 드래그하거나 X / Y를 직접 입력합니다.
- **핫스팟 보정**: 내 커서로 점 5개를 차례로 클릭하면 끝부분이 얼마나 어긋났는지 측정해 핫스팟을 자동으로 보정합니다. 적용만 하면 됩니다.
- **그리기 스타일**: 모드마다 *고해상도*(부드러운 테두리), *픽셀*(픽셀이 보이는 스타일), *계단 테두리*(옛날 커서 같은 딱딱한 테두리) 중에서 고를 수 있으며, 미리보기에 Windows가 실제로 그리는 모습이 표시됩니다.
- **포인터 크기**: Windows 설정과 같은 전역 크기(32–256px)를 사용하며 서로 동기화됩니다.
- **되돌리기**: *마지막 변경 취소*와 *Windows 기본 포인터로 복원*을 클릭 한 번으로 실행할 수 있습니다.
- **라이트와 다크**: 푸딩 색상의 테마를 제공하며, 繁體中文, 简体中文, English, 日本語, 한국어를 지원합니다.

## 다운로드

1. [Releases](https://github.com/Yucheng0208/Purin-Pointer/releases/latest)에서 ZIP 파일을 다운로드합니다.
   - `PurinPointer-win-x64.zip`: 대부분의 Windows PC(Intel / AMD)
   - `PurinPointer-win-arm64.zip`: ARM 노트북(Snapdragon 등)
2. 압축을 풀고 `PurinPointer.exe`를 실행합니다. 설치할 필요가 없고 .NET을 따로 설치할 필요도 없습니다.
3. **Windows의 PC 보호** 화면이 나타나면 **추가 정보 → 실행**을 클릭합니다.

> [!IMPORTANT]
> 릴리스는 **코드 서명되어 있지 않습니다**. **스마트 앱 컨트롤**이 켜져 있으면 Windows 11은 서명되지 않은 앱을 바로 차단합니다. 이 경우 [소스에서 빌드](docs/engineering.ko.md#build-and-run)하세요.

Windows 10 또는 Windows 11이 필요합니다.

## 빠른 시작

1. 모드 탭을 고릅니다. 예를 들면 **기본 포인터**입니다.
2. 이미지, GIF 또는 `.ani` 파일을 **01 커서 가져오기**에 끌어다 놓습니다.
3. 배경이 있다면 **04 배경 제거**에서 **자동 제거**를 클릭합니다.
4. 미리보기에서 핫스팟 십자 표시를 포인터 끝으로 드래그합니다.
5. **테마 적용**을 클릭합니다(또는 **Ctrl + Enter**를 누릅니다).

전체 사용법은 [사용자 가이드](docs/user-guide.ko.md)에 있습니다.

## 문서

| 문서 | 내용 |
| --- | --- |
| [사용자 가이드](docs/user-guide.ko.md) | 모든 화면과 기능을 단계별로 설명하고 문제 해결 방법을 안내합니다 |
| [엔지니어링 가이드](docs/engineering.ko.md) | 아키텍처, 빌드, 테스트, Windows 연동, 릴리스 |

## 소스에서 빌드

[.NET 10 SDK](https://dotnet.microsoft.com/download)가 필요합니다.

```powershell
dotnet run --project src/PurinPointer
```

```powershell
dotnet run --project tests/PurinPointer.Tests
```

게시 방법과 프로젝트 구조는 [엔지니어링 가이드](docs/engineering.ko.md)를 참고하세요.

## 라이선스

[GPL-3.0](LICENSE)
