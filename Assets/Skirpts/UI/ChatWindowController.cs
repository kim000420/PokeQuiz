// Assets/Scripts/UI/ChatWindowController.cs

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

public class ChatWindowController : MonoBehaviour
{
    [Header("Target Input Field")]
    [Tooltip("채팅 입력창 (이벤트를 감지하기 위함)")]
    [SerializeField] private TMP_InputField inputField;

    [Header("Settings")]
    [Tooltip("채팅창 RectTransform")]
    [SerializeField] private RectTransform chatPanelRect;

    [Tooltip("최대 높이 (펼쳤을 때)")]
    [SerializeField] private float expandedHeight = 1900f; 

    [Tooltip("애니메이션 시간")]
    [SerializeField] private float animDuration = 0.3f;

    [Tooltip("애니메이션 효과")]
    [SerializeField] private Ease animEase = Ease.OutBack;

    [Header("Canvas")]
    [Tooltip("캔버스 (픽셀 단위를 UI 단위로 변환하기 위해 필요)")]
    [SerializeField] private Canvas parentCanvas;

    private bool _isExpanded = true; // 현재 상태 (기본은 펼쳐짐)
    public bool IgnoreNextEndEdit { get; set; } = false;
    public bool BlockNextExpand { get; set; } = false;

    private void Start()
    {
        // InputField 이벤트 연결
        if (inputField != null)
        {
            // 입력창을 터치했을 때 (포커스 잡힘) -> 축소
            inputField.onSelect.AddListener(delegate { SetWindowState(false); });

            // 입력이 끝났을 때 (엔터, 전송, 포커스 잃음) -> 확대
            // 주의: 전송 버튼을 눌렀을 때도 이 이벤트가 발생합니다.
            inputField.onEndEdit.AddListener(HandleEndEdit);
        }
    }

    // OnEndEdit 처리 로직 분리
    private void HandleEndEdit(string text)
    {
        StartCoroutine(CheckAndExpandRoutine());
    }

    /// <summary>
    /// 버튼용 토글 함수
    /// </summary>
    public void ToggleChatWindow()
    {
        SetWindowState(!_isExpanded);
    }

    /// <summary>
    /// [새 함수] 원하는 상태로 강제 전환하는 함수
    /// </summary>
    /// <param name="expand">true면 확대, false면 축소</param>
    public void SetWindowState(bool expand)
    {
        _isExpanded = expand;
        float targetHeight;

        if (_isExpanded)
        {
            targetHeight = expandedHeight;
        }
        else
        {
            float pixelKeyboardHeight = KeyboardUtils.GetKeyboardHeight();
            if (pixelKeyboardHeight <= 0) pixelKeyboardHeight = 700f; // 테스트값

            float uiKeyboardHeight = pixelKeyboardHeight / parentCanvas.scaleFactor;
            float screenHeightUI = Screen.height / parentCanvas.scaleFactor;
            float safeAreaTop = 200f;

            float calculatedHeight = screenHeightUI - uiKeyboardHeight - safeAreaTop;

            // 남은 공간 = 전체 - 키보드 - 상단여백
            targetHeight = Mathf.Min(calculatedHeight, expandedHeight);

            // 너무 작아지면 최소 입력창 크기(150)는 유지
            if (targetHeight < 150f) targetHeight = 150f;
        }

        // 높이 애니메이션
        chatPanelRect.DOSizeDelta(new Vector2(chatPanelRect.sizeDelta.x, targetHeight), animDuration)
                     .SetEase(animEase);
    }

    // 메모리 누수 방지를 위한 리스너 해제 (선택 사항이지만 권장)
    private void OnDestroy()
    {
        if (inputField != null)
        {
            inputField.onSelect.RemoveAllListeners();
            inputField.onEndEdit.RemoveAllListeners();
        }
    }

    // 판단 유예 코루틴
    private IEnumerator CheckAndExpandRoutine()
    {
        // 1. 한 프레임 대기 (이 사이에 전송 버튼의 OnClick이 실행되어 플래그를 켤 기회를 줌)
        yield return null;

        // 2. 플래그 확인
        if (BlockNextExpand)
        {
            // [중요] 플래그를 사용했으니 다시 껍니다 (Reset)
            BlockNextExpand = false;

            // (선택) 확실하게 축소 상태 유지
            SetWindowState(false);
        }
        else
        {
            // 전송 버튼 안 눌림 (진짜로 입력 종료) -> 확대
            SetWindowState(true);
        }
    }
}