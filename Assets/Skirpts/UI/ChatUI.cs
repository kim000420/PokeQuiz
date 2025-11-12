// Assets/Scripts/UI/ChatUI.cs

using UnityEngine;
using TMPro; // TextMeshPro (TMP) UI를 사용하기 위해
using UnityEngine.UI; // Button, ScrollRect
using System.Collections.Generic; // 채팅 로그 관리를 위해

/// <summary>
/// [옵저버] '2. Main Chat UI'를 관리합니다.
/// NetworkManager의 이벤트를 '구독'하여 채팅 로그, 입력창 등을 제어합니다.
/// </summary>
public class ChatUI : MonoBehaviour
{
    [Header("UI 컴포넌트 연결")]
    [Tooltip("님이 11-A 단계에서 만든 '채팅 한 줄' 프리팹")]
    [SerializeField] private GameObject chatMessagePrefab;

    [Tooltip("채팅 프리팹이 생성될 Scroll View의 'Content' 오브젝트")]
    [SerializeField] private Transform chatContentTransform;

    [Tooltip("채팅 입력창 (TMP_InputField)")]
    [SerializeField] private TMP_InputField chatInputField;

    [Tooltip("전송 버튼 (Button)")]
    [SerializeField] private Button sendButton;

    [Tooltip("스크롤 뷰의 ScrollRect 컴포넌트 (자동 스크롤용)")]
    [SerializeField] private ScrollRect chatScrollRect;

    [Tooltip("(선택) 서버 연결 상태를 표시할 텍스트")]
    [SerializeField] private TMP_Text statusText;

    private List<GameObject> chatLog = new List<GameObject>(); // 생성된 메시지 관리

    // --- 1. 옵저버 패턴 (이벤트 구독) ---

    private void OnEnable()
    {
        // NetworkManager의 '신호(이벤트)'를 '구독'합니다.
        NetworkManager.OnMessageReceived += HandleServerMessage;
        NetworkManager.OnConnectionStateChanged += HandleConnectionState;

        // 버튼 클릭 이벤트와 입력창 'Enter' 이벤트에 '메시지 전송' 함수를 연결
        sendButton.onClick.AddListener(OnSendButtonClicked);
        chatInputField.onSubmit.AddListener(OnInputFieldSubmit);
    }

    private void OnDisable()
    {
        // 오브젝트가 비활성화되면 '구독'을 '해제'합니다. (메모리 누수 방지)
        NetworkManager.OnMessageReceived -= HandleServerMessage;
        NetworkManager.OnConnectionStateChanged -= HandleConnectionState;

        sendButton.onClick.RemoveListener(OnSendButtonClicked);
        chatInputField.onSubmit.RemoveListener(OnInputFieldSubmit);
    }

    // --- 2. 이벤트 핸들러 (신호 수신) ---

    /// <summary>
    /// NetworkManager로부터 '메시지 수신' 신호를 받았을 때 호출됩니다.
    /// </summary>
    private void HandleServerMessage(string message)
    {
        // 퀴즈 시작/힌트 관련 메시지는 PopupManager가 전담하므로,
        // 채팅 로그에서는 이 메시지들을 '무시(return)'합니다.
        if (message.StartsWith("[퀴즈] 새 퀴즈를") ||
            message.StartsWith("[퀴즈] 문제를 가져왔습니다") ||
            message.StartsWith("[힌트]"))
        {
            return; // 채팅 로그에 추가하지 않고 무시
        }
        // 채팅 로그에도 표시되어야 하므로 '무시'하지 않습니다.
        if (message.StartsWith("[정답!]"))
        {
            // PopupManager가 이 메시지를 별도로 처리할 것입니다.
            // (여기서는 채팅창에만 추가)
        }

        // [수정됨]
        // 힌트가 아닌 모든 메시지(시스템, 채팅, 정답, 시간 초과 등)는 로그에 추가합니다.
        // (색상 구분을 위한 간단한 로직 추가)
        if (message.StartsWith("[시스템]") || message.StartsWith("[서버]"))
        {
            AddMessageToChatLog(message, Color.green);
        }
        else if (message.StartsWith("[정답!]") || message.StartsWith("[시간 초과]"))
        {
            AddMessageToChatLog(message, Color.yellow);
        }
        else
        {
            AddMessageToChatLog(message, Color.white);
        }
    }

    /// <summary>
    /// NetworkManager로부터 '연결 상태 변경' 신호를 받았을 때 호출됩니다.
    /// </summary>
    private void HandleConnectionState(bool isConnected)
    {
        chatInputField.interactable = isConnected; // 연결되면 입력창 활성화
        sendButton.interactable = isConnected;     // 연결되면 버튼 활성화

        if (statusText != null)
        {
            statusText.text = isConnected ? "서버: 온라인" : "서버: 오프라인";
            statusText.color = isConnected ? Color.green : Color.red;
        }

        if (isConnected)
        {
            AddMessageToChatLog("[시스템] 서버에 연결되었습니다.", Color.green);
        }
        else
        {
            AddMessageToChatLog("[시스템] 서버와 연결이 끊겼습니다.", Color.red);
        }
    }

    // --- 3. UI 조작 (입력/출력) ---

    /// <summary>
    /// 전송 버튼(Btn_Send)을 클릭했을 때 호출됩니다.
    /// </summary>
    private void OnSendButtonClicked()
    {
        SendChatMessage();
    }

    /// <summary>
    /// 입력창(TMP_InputField)에서 'Enter' 키를 눌렀을 때 호출됩니다.
    /// </summary>
    private void OnInputFieldSubmit(string text)
    {
        // 'Shift + Enter' (줄바꿈)가 아닐 때만 전송
        if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
        {
            SendChatMessage();
        }
    }

    /// <summary>
    /// (핵심) 입력창의 텍스트를 NetworkManager로 전송합니다.
    /// </summary>
    private void SendChatMessage()
    {
        string message = chatInputField.text.Trim();

        if (!string.IsNullOrEmpty(message))
        {
            // UI 스크립트는 서버 통신을 '직접' 하지 않습니다.
            // 싱글톤 NetworkManager에 '요청'만 보냅니다.
            NetworkManager.Instance.SendChatMessage(message);

            // 입력창 초기화
            chatInputField.text = "";

            // 전송 후에도 입력창에 다시 포커스
            chatInputField.ActivateInputField();
        }
    }

    /// <summary>
    /// (핵심) 서버 메시지를 받아 '채팅 프리팹'을 생성하고 로그에 추가합니다.
    /// </summary>
    private void AddMessageToChatLog(string message, Color color)
    {
        if (chatMessagePrefab == null || chatContentTransform == null)
        {
            Debug.LogError("[ChatUI] 프리팹 또는 Content가 연결되지 않았습니다!");
            return;
        }

        // 1. 11-A 단계에서 만든 '채팅 프리팹'을 'Content' 자식으로 생성
        GameObject newMsg = Instantiate(chatMessagePrefab, chatContentTransform);
        chatLog.Add(newMsg); // (선택) 로그 관리를 위해 리스트에 추가

        // 2. 생성된 프리팹에서 TMP_Text 컴포넌트를 찾아 텍스트 설정
        TMP_Text tmpText = newMsg.GetComponent<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = message;
            tmpText.color = color; // [수정됨] 색상 적용
        }

        // 3. (자동 스크롤) 새 메시지가 추가되면 스크롤을 맨 아래로 내림
        // 레이아웃이 갱신된 '다음 프레임'에 스크롤을 이동해야 정확합니다.
        StartCoroutine(ScrollToBottom());
    }

    private System.Collections.IEnumerator ScrollToBottom()
    {
        // 다음 프레임까지 대기
        yield return null;

        if (chatScrollRect != null)
        {
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}