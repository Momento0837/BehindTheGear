# Pixel Bridge Layer Sprites

Unity 6에서 Aseprite 원본의 첫 프레임을 레이어별 스프라이트로 가져옵니다. 원본 캔버스 크기와 중앙 피벗을 공유하므로 같은 위치에 놓으면 각 그림이 원래 위치에 맞습니다.

## 설치와 사용

1. `PixelBridgeLayerSprites` 폴더를 프로젝트의 `Packages/com.local.pixelbridge-layer-sprites`로 복사합니다. 외부 폴더에 두고 쓰려면 Unity의 **Window → Package Manager → + → Install package from disk**에서 이 폴더의 `package.json`을 선택해도 됩니다.
2. Unity Project 창에서 `Assets` 아래의 `.aseprite` 또는 `.ase` 원본을 우클릭하고 **Pixel Bridge → Import Visible Layer Sprites**를 선택합니다. Aseprite 1.3 이상이 필요합니다. Steam 설치는 자동으로 찾고, 찾지 못하면 실행 파일 선택 창이 열립니다.
3. 원본 옆에 생긴 `.sprites` 폴더를 엽니다. 예를 들어 `Inventory.aseprite.sprites` 폴더에는 각 레이어가 별도의 PNG 파일로 들어 있습니다. 각 파일의 **Texture Type**은 **Sprite (2D and UI)**, **Sprite Mode**는 **Single**입니다. 원하는 파일을 SpriteRenderer의 **Sprite** 필드나 UI Image의 **Source Image** 필드로 끌어 놓으세요.

눈 아이콘이 켜진 그림 레이어 중 첫 프레임에 보이는 픽셀이 있는 레이어만 가져옵니다. 숨긴 그룹의 자손과 참조 레이어도 제외합니다. 그룹 경로를 파일명에 포함하므로 `장비 / 손 / 칼`은 `장비_손_칼.png`로 저장됩니다. 텍스처 필터는 Point이며 기본값은 100 PPU입니다.

원본을 저장하고 Unity가 새로 고치면 PNG가 갱신됩니다. 같은 레이어의 그림을 수정하면 파일 참조가 유지됩니다. 레이어를 숨기거나 삭제하면 도구가 생성한 해당 PNG도 삭제되므로 이를 사용하던 오브젝트를 확인하세요. 다시 표시하면 PNG가 새로 생성됩니다.

기존 PNG의 Inspector에서 설정한 PPU, Filter Mode, Compression, 피벗, 슬라이스, 플랫폼별 설정은 재출력 시 유지됩니다. `.pblayers`의 Pixels Per Unit과 기본 임포트 설정은 **새 PNG를 처음 생성할 때만** 적용합니다. 기존 PNG 설정을 바꾸려면 해당 PNG의 Inspector에서 직접 수정하세요.

원본 옆의 `.pblayers`는 자동 갱신을 위한 연결 에셋입니다. 이전 버전에서 이 에셋 안의 Sprite를 연결한 경우에도 기존 참조를 사용할 수 있습니다. 팀 저장소에는 원본, `.pblayers`, `.sprites` 폴더와 각각의 `.meta`를 함께 넣으세요. 실행 파일 경로는 각 컴퓨터에서 **Tools → Pixel Bridge → Layer Sprites → Select Aseprite**로 지정할 수 있습니다.
