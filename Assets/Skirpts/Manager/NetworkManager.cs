// Assets/Scripts/Managers/NetworkManager.cs

using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Concurrent;

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

    // --- 2. 옵저버 패턴 (이벤트) ---
    /// <summary>
    /// [핵심] 서버에서 메시지(채팅, 힌트, 정답)가 수신될 때마다 발생하는 이벤트입니다.
    /// UI(옵저버)들이 이 이벤트를 '구독'합니다.
    /// </summary>
    public static event Action<string> OnMessageReceived;
    //서버 연결 상태가 변경될 때 발생하는 이벤트입니다.
    public static event Action<bool> OnConnectionStateChanged;
    // 유저 목록 변경 이벤트
    public static event Action<System.Collections.Generic.List<UserData>> OnUserListReceived;
    // 유저수  갱신 이벤트
    public static event Action<string> OnUserCountUpdated;
    // 내 점수 갱신 이벤트
    public static event Action<int> OnMyScoreReceived;


    [Header("서버 정보")]
    [SerializeField] private string serverIP = "34.22.102.159"; // [중요] 님의 VM 공용 IP
    [SerializeField] private int serverPort = 7777; // [중요] 님의 서버 포트

    [Header("로그인 (기능 1)")]
    [SerializeField] private string nickname = "유니티테스터"; // [중요] 서버로 보낼 닉네임

    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isConnected = false;

    // --- Unity 생명주기 ---
    private void Awake()
    {
        // 싱글톤 인스턴스 관리
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(this.gameObject); // 씬이 바뀌어도 파괴되지 않음
    }

    private async void Start()
    {
        // 게임이 시작되면 자동으로 서버에 접속 시도
        await ConnectToServerAsync();
    }

    private void OnDestroy()
    {
        // 게임 종료 시 연결 해제
        DisconnectFromServer();
    }

    // --- 핵심 TCP 통신 로직 ---

    /// <summary>
    /// 서버에 접속하고 닉네임을 전송합니다.
    /// </summary>
    private async Task ConnectToServerAsync()
    {
        if (_isConnected) return;

        try
        {
            _client = new TcpClient();
            Debug.Log($"[NetworkManager] 서버 접속 시도: {serverIP}:{serverPort}");
            await _client.ConnectAsync(serverIP, serverPort);
            _stream = _client.GetStream();
            _isConnected = true;

            // 연결 성공 이벤트 방송
            MainThreadDispatcher.ExecuteOnMainThread(() =>
                OnConnectionStateChanged?.Invoke(true)
            );

            // 접속 직후, 닉네임을 서버로 전송
            await SendMessageToServerAsync(nickname);

            // 서버로부터 메시지를 계속 수신하는 루프 시작
            _ = ReceiveMessagesAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 서버 접속 실패: {e.Message}");
            MainThreadDispatcher.ExecuteOnMainThread(() =>
                OnConnectionStateChanged?.Invoke(false)
            );
        }
    }

    /// <summary>
    /// 서버로부터 메시지를 '수신'하는 비동기 루프입니다.
    /// </summary>
    private async Task ReceiveMessagesAsync()
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (_isConnected)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    // 서버가 연결을 정상적으로 끊음
                    Debug.LogWarning("[NetworkManager] 서버가 연결을 끊었습니다.");
                    DisconnectFromServer();
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                // '범용' 이벤트(OnMessageReceived)로 가기 전에,
                // '특수 태그'들을 먼저 모두 필터링합니다.

                if (message.StartsWith("[USER_COUNT]"))
                {
                    // 예: "[USER_COUNT] 2/6" -> "2/6"
                    string countStr = message.Substring("[USER_COUNT]".Length).Trim();
                    MainThreadDispatcher.ExecuteOnMainThread(() => OnUserCountUpdated?.Invoke(countStr));
                    continue; // 채팅 로그에는 표시 안 함
                }

                if (message.StartsWith("[USER_LIST]"))
                {
                    // 예: "[USER_LIST] A:3,B:0"
                    string dataStr = message.Substring("[USER_LIST]".Length).Trim();
                    var userList = new System.Collections.Generic.List<UserData>();

                    if (!string.IsNullOrEmpty(dataStr))
                    {
                        string[] users = dataStr.Split(',');
                        foreach (var userStr in users)
                        {
                            // [수정됨] "닉:승" (2개) 파싱
                            string[] parts = userStr.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int score))
                            {
                                userList.Add(new UserData { Nickname = parts[0], Score = score });
                            }
                        }
                    }
                    MainThreadDispatcher.ExecuteOnMainThread(() => OnUserListReceived?.Invoke(userList));
                    continue; // 처리 완료. 범용 이벤트로 보내지 않음.
                }

                // 퀴즈/서버 메시지도 '범용' 이벤트로 보내지 않습니다.
                // (ChatUI가 아닌 PopupManager가 처리해야 함)
                if (message.StartsWith("[퀴즈]") ||
                    message.StartsWith("[힌트]") ||
                    message.StartsWith("[정답!]") ||
                    message.StartsWith("[시간 초과]") ||
                    message.StartsWith("[서버]") ||
                    message.StartsWith("[오류]"))
                {
                    // (PopupManager와 ChatUI의 HandleServerMessage가 이 메시지들을 받을 것임)
                }

                // '모든' 메시지를 범용 이벤트로 보내는 대신,
                // '필터링되고 남은' 메시지(즉, 진짜 유저 채팅)만 보냅니다.
                if (message.StartsWith("["))
                {
                    // (이 코드는 ChatUI가 구독 중)
                    MainThreadDispatcher.ExecuteOnMainThread(() =>
                        OnMessageReceived?.Invoke(message)
                    );
                }
                else
                {
                    Debug.LogWarning($"[NetworkManager] 태그가 없는 메시지 수신: {message}");
                }
            }
        }
        catch (Exception e)
        {
            // 네트워크 오류로 연결 끊김
            if (_isConnected) // 우리가 끈 게 아니라면
            {
                Debug.LogError($"[NetworkManager] 메시지 수신 오류: {e.Message}");
                DisconnectFromServer();
            }
        }
    }

    /// <summary>
    /// (public) UI(옵저버)가 호출할 메시지 '전송' 함수입니다. (채팅, /퀴즈시작)
    /// </summary>
    public void SendChatMessage(string message)
    {
        if (!_isConnected || string.IsNullOrEmpty(message)) return;

        // UI 스레드에서 호출하므로, Task로 감싸서 비동기 실행
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
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 메시지 전송 오류: {e.Message}");
            DisconnectFromServer(); // 전송 실패 시 연결 끊김 처리
        }
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

        Debug.LogWarning("[NetworkManager] 서버 연결 종료.");
        MainThreadDispatcher.ExecuteOnMainThread(() =>
            OnConnectionStateChanged?.Invoke(false)
        );
    }
}