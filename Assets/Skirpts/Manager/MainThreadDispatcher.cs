// Assets/Scripts/Managers/MainThreadDispatcher.cs

using System;
using System.Collections.Concurrent; // 스레드 안전한 큐(Queue)
using UnityEngine;

/// <summary>
/// [싱글톤 헬퍼] 백그라운드 스레드(예: TCP 수신)의 작업을
/// Unity '메인 스레드'에서 안전하게 실행할 수 있도록 중계하는 '우체통'입니다.
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    // --- 1. 싱글톤 설정 ---
    private static MainThreadDispatcher _instance;
    private static readonly object _lock = new object();

    // 스레드 안전한 '작업(Action) 큐'
    private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();

    public static MainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        // 씬에서 인스턴스를 찾거나, 없으면 새로 생성
                        _instance = FindAnyObjectByType<MainThreadDispatcher>();
                        if (_instance == null)
                        {
                            GameObject go = new GameObject("MainThreadDispatcher");
                            _instance = go.AddComponent<MainThreadDispatcher>();
                        }
                    }
                }
            }
            return _instance;
        }
    }

    // --- 2. Unity 생명주기 (메인 스레드) ---
    private void Awake()
    {
        // 싱글톤 인스턴스 관리
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject); // 씬이 바뀌어도 파괴되지 않음
        }
    }

    /// <summary>
    /// 메인 스레드(Update)에서 매 프레임 실행됩니다.
    /// </summary>
    private void Update()
    {
        // '우체통(큐)'에 작업이 쌓여있으면, 하나씩 꺼내서 '실행'합니다.
        while (_executionQueue.TryDequeue(out Action action))
        {
            action.Invoke();
        }
    }

    // --- 3. 핵심 기능 (외부 호출) ---

    /// <summary>
    /// [핵심] 백그라운드 스레드에서 이 함수를 호출하여,
    /// 메인 스레드에서 실행할 작업을 '예약(Enqueue)'합니다.
    /// (예: NetworkManager의 ReceiveMessagesAsync)
    /// </summary>
    /// <param name="action">메인 스레드에서 실행될 작업</param>
    public static void ExecuteOnMainThread(Action action)
    {
        if (action == null)
        {
            Debug.LogWarning("MainThreadDispatcher: null Action이 예약되었습니다.");
            return;
        }

        // 큐에 작업 추가
        _executionQueue.Enqueue(action);
    }
}