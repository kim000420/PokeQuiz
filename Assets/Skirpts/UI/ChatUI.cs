// Assets/Scripts/UI/ChatUI.cs

using UnityEngine;
using TMPro; // TextMeshPro (TMP) UI를 사용하기 위해
using UnityEngine.UI; // Button, ScrollRect
using SharedPackets; // 패킷 사용

/// <summary>
/// [옵저버] '2. Main Chat UI'를 관리합니다.
/// NetworkManager의 이벤트를 '구독'하여 채팅 로그, 입력창 등을 제어합니다.
/// </summary>
public class ChatUI : MonoBehaviour
{
    [Header("UI 컴포넌트 연결")]
    [Tooltip("채팅 프리팹")]
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

    private void OnEnable()
    {
        // NetworkManager의 '신호(이벤트)'를 '구독'합니다.
        NetworkManager.OnChatMessageReceived += HandleChatMessage;
        NetworkManager.OnConnectionStateChanged += HandleConnectionState;

        // 버튼 클릭 이벤트와 입력창 'Enter' 이벤트에 '메시지 전송' 함수를 연결
        sendButton.onClick.AddListener(SendChatMessage);
        chatInputField.onSubmit.AddListener(delegate { SendChatMessage(); });
    }

    private void OnDisable()
    {
        // 오브젝트가 비활성화되면 '구독'을 '해제'합니다. (메모리 누수 방지)
        NetworkManager.OnChatMessageReceived -= HandleChatMessage;
        NetworkManager.OnConnectionStateChanged -= HandleConnectionState;

        sendButton.onClick.RemoveListener(SendChatMessage);
        chatInputField.onSubmit.RemoveListener(delegate { SendChatMessage(); });
    }

    /// <summary>
    /// NetworkManager로부터 '메시지 수신' 신호를 받았을 때 호출됩니다.
    /// </summary>
    private void HandleChatMessage(ChatPacket pkt)
    {
        Color color = Color.white;
        // 헥사 코드(#RRGGBB)를 컬러로 변환
        if (!string.IsNullOrEmpty(pkt.colorHex))
        {
            ColorUtility.TryParseHtmlString(pkt.colorHex, out color);
        }
        AddMessageToChatLog(pkt.message, color);
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
    /// 입력창의 텍스트를 NetworkManager로 전송합니다.
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
    /// 서버 메시지를 받아 '채팅 프리팹'을 생성하고 로그에 추가합니다.
    /// </summary>
    private void AddMessageToChatLog(string message, Color color)
    {
        if (chatMessagePrefab == null) return;

        GameObject newMsg = Instantiate(chatMessagePrefab, chatContentTransform);

        TMP_Text tmpText = newMsg.GetComponent<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = message;
            tmpText.color = color;
        }

        // 자동 스크롤
        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }
}