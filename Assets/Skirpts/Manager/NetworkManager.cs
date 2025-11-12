// Assets/Scripts/Managers/NetworkManager.cs

using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Concurrent; // 10단계의 MainThreadDispatcher를 위해

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
                _instance = FindObjectOfType<NetworkManager>();
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

    /// <summary>
    /// 서버 연결 상태가 변경될 때 발생하는 이벤트입니다.
    /// </summary>
    public static event Action<bool> OnConnectionStateChanged;


    [Header("서버 정보")]
    [SerializeField] private string serverIP = "34.22.102.159"; // [중요] 님의 VM 공용 IP
    [SerializeField] private int serverPort = 7777; // [중요] 님의 서버 포트

    [Header("로그인 (기능 1)")]
    [SerializeField] private string nickname = "유니티테스터"; // [중요] 서버로 보낼 닉네임

    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isConnected = false;

    // --- 3. Unity 생명주기 ---
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

    // --- 4. 핵심 TCP 통신 로직 ---

    /// <summary>
    /// (기능 1) 서버에 접속하고 닉네임을 전송합니다.
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

            // (기능 1) 접속 직후, 닉네임을 서버로 전송
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

                // [핵심] 메시지를 UI로 직접 보내지 않고, '메인 스레드'에서 '이벤트'를 방송(Invoke)합니다.
                MainThreadDispatcher.ExecuteOnMainThread(() =>
                    OnMessageReceived?.Invoke(message)
                );
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