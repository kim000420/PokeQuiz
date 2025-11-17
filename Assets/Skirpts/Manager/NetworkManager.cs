// Assets/Scripts/Managers/NetworkManager.cs

using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using SharedPackets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; 
// 유저 목록 데이터를 전달하기 위한 간단한 클래스
public class UserData
{
    public string Nickname;
    public int Score;
}

/// <summary>
/// [싱글톤] VM 서버와의 모든 TCP 통신을 전담하는 '주체(Subject)'입니다.
/// 이 스크립트는 UI를 전혀 모르며, 오직 '신호(Event)'만 보냅니다.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    // --- 1. 싱글톤 설정 ---
    private static NetworkManager _instance;
    public static NetworkManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // 씬에서 인스턴스를 찾거나, 없으면 새로 생성
                _instance = FindAnyObjectByType<NetworkManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("NetworkManager");
                    _instance = go.AddComponent<NetworkManager>();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 서버에서 메시지(채팅, 힌트, 정답)가 수신될 때마다 발생하는 이벤트입니다.
    /// UI(옵저버)들이 이 이벤트를 '구독'합니다.
    /// </summary>
    public static event Action<bool> OnConnectionStateChanged;

    // UI별로 필요한 패킷만 전달하는 명확한 이벤트들
    public static event Action<ChatPacket> OnChatMessageReceived;
    public static event Action<UserCountPacket> OnUserCountUpdated;
    public static event Action<UserListPacket> OnUserListReceived;
    public static event Action<QuizStartPacket> OnQuizStarted;
    public static event Action<HintPacket> OnHintReceived;
    public static event Action<WinnerPacket> OnWinnerReceived;
    public static event Action<QuizEndPacket> OnQuizEnded; 
    public static event Action<LoginResponsePacket> OnLoginResultReceived;


    [Header("서버 정보")]
    [SerializeField] private string serverIP = "34.22.102.159"; // VM 공용 IP
    [SerializeField] private int serverPort = 7777; // 서버 포트
    
    private string nickname; // 서버로 보낼 닉네임
    public string MyNickname => nickname;

    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isConnected = false;

    // --- Unity 생명주기 ---
    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this.gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    private async void Start() { }

    private void OnDestroy()
    {
        DisconnectFromServer();
    }
    // --- 핵심 TCP 통신 로직 ---

    /// <summary>
    /// 서버에 접속하고 닉네임을 전송합니다.
    /// </summary>
    public async Task ConnectAndLoginAsync(string inputNickname)
    {
        if (_isConnected) return;

        this.nickname = inputNickname; // 입력받은 닉네임 저장

        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(serverIP, serverPort);
            _stream = _client.GetStream();
            _isConnected = true;

            // 연결 성공 이벤트 방송
            MainThreadDispatcher.ExecuteOnMainThread(() =>
                OnConnectionStateChanged?.Invoke(true)
            );

            // 접속 직후, 닉네임을 서버로 전송
            var loginPkt = new LoginRequestPacket
            {
                type = "LOGIN_REQ",
                nickname = this.nickname
            };
            string json = JsonConvert.SerializeObject(loginPkt);
            await SendMessageToServerAsync(json);

            // 서버로부터 메시지를 계속 수신하는 루프 시작
            _ = ReceiveMessagesAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 서버 접속 실패: {e.Message}");
            _isConnected = false;
            MainThreadDispatcher.ExecuteOnMainThread(() =>
            {
                OnConnectionStateChanged?.Invoke(false);
                // 로그인 실패 이벤트 발생 (UI에 알림)
                OnLoginResultReceived?.Invoke(new LoginResponsePacket { success = false, message = "서버 연결 실패" });
            });
        }
    }

    /// <summary>
    /// 서버로부터 메시지를 '수신'하는 비동기 루프입니다.
    /// </summary>
    private async Task ReceiveMessagesAsync()
    {
        byte[] buffer = new byte[4096];
        string incompleteMessage = "";
        try
        {
            while (_isConnected)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    DisconnectFromServer();
                    break;
                }

                string receivedData = incompleteMessage + Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // [핵심] '\n'을 기준으로 메시지 분리 (JSON 경계 처리)
                string[] messages = receivedData.Split(new[] { '\n' }, StringSplitOptions.None);

                // 마지막 조각 제외하고 처리 (마지막 조각은 다음 데이터와 이어질 수 있음)
                for (int i = 0; i < messages.Length - 1; i++)
                {
                    if (!string.IsNullOrEmpty(messages[i]))
                    {
                        HandleJsonMessage(messages[i]);
                    }
                }

                // 남은 조각 저장
                incompleteMessage = messages[messages.Length - 1];
            }
        }
        catch (Exception e)
        {
            if (_isConnected) { Debug.LogError($"수신 오류: {e.Message}"); DisconnectFromServer(); }
        }
    }

    private void HandleJsonMessage(string jsonMessage)
    {
        try
        {
            // Type 확인
            JObject jsonObj = JObject.Parse(jsonMessage);
            string messageType = jsonObj["type"]?.ToString();

            if (string.IsNullOrEmpty(messageType)) return;

            // 메인 스레드에서 이벤트 발생
            MainThreadDispatcher.ExecuteOnMainThread(() =>
            {
                try
                {
                    switch (messageType)
                    {
                        case "CHAT":
                            OnChatMessageReceived?.Invoke(jsonObj.ToObject<ChatPacket>());
                            break;
                        case "USER_COUNT":
                            OnUserCountUpdated?.Invoke(jsonObj.ToObject<UserCountPacket>());
                            break;
                        case "USER_LIST":
                            OnUserListReceived?.Invoke(jsonObj.ToObject<UserListPacket>());
                            break;
                        case "QUIZ_START":
                            OnQuizStarted?.Invoke(jsonObj.ToObject<QuizStartPacket>());
                            break;
                        case "HINT":
                            OnHintReceived?.Invoke(jsonObj.ToObject<HintPacket>());
                            break;
                        case "WINNER":
                            OnWinnerReceived?.Invoke(jsonObj.ToObject<WinnerPacket>());
                            break;
                        case "QUIZ_END":
                            OnQuizEnded?.Invoke(jsonObj.ToObject<QuizEndPacket>());
                            break;
                        case "LOGIN_RES":
                            OnLoginResultReceived?.Invoke(jsonObj.ToObject<LoginResponsePacket>());
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"이벤트 처리 실패: {e.Message}");
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON 파싱 실패: {jsonMessage} / {e.Message}");
        }
    }

    /// <summary>
    /// (public) UI(옵저버)가 호출할 메시지 '전송' 함수입니다. (채팅, /퀴즈시작)
    /// </summary>
    public void SendChatMessage(string message)
    {
        if (!_isConnected || string.IsNullOrEmpty(message)) return;
        _ = SendMessageToServerAsync(message);
    }

    /// <summary>
    /// (private) 실제 바이트 데이터를 서버로 전송하는 내부 함수입니다.
    /// </summary>
    private async Task SendMessageToServerAsync(string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            await _stream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception) { DisconnectFromServer(); }
    }

    /// <summary>
    /// 연결을 안전하게 종료합니다.
    /// </summary>
    private void DisconnectFromServer()
    {
        if (!_isConnected) return;
        _isConnected = false;
        _stream?.Close();
        _client?.Close();
        MainThreadDispatcher.ExecuteOnMainThread(() 
            => OnConnectionStateChanged?.Invoke(false));
    }
}