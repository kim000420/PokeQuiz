using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// [옵저버] 우측 하단 '접속자 수'와 '내 점수' UI를 관리합니다.
/// </summary>
public class UserListUI : MonoBehaviour
{
    [Header("UI 연결")]
    [Tooltip("접속자 수를 표시할 텍스트 (예: Online: 5)")]
    [SerializeField] private TMP_Text userCountText;
    [SerializeField] private List<TMP_Text> userCountText;

    [Tooltip("내 점수를 표시할 텍스트 (예: Score: 10)")]
    [SerializeField] private TMP_Text myScoreText;

    private void OnEnable()
    {
        // NetworkManager 이벤트 구독
        NetworkManager.OnUserCountReceived += UpdateUserCount;
        NetworkManager.OnMyScoreReceived += UpdateMyScore;
    }

    private void OnDisable()
    {
        NetworkManager.OnUserCountReceived -= UpdateUserCount;
        NetworkManager.OnMyScoreReceived -= UpdateMyScore;
    }

    private void UpdateUserCount(int count)
    {
        if (userCountText != null)
        {
            userCountText.text = $"접속자: {count}명";
        }
    }

    private void UpdateMyScore(int score)
    {
        if (myScoreText != null)
        {
            myScoreText.text = $"내 점수: {score}승";
        }
    }
}