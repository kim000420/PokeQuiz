using UnityEngine;
using TMPro;
using System.Collections.Generic;
using SharedPackets;

/// <summary>
/// [옵저버] 접속자 명단과 점수를 표시하는 UI (Scroll View)
/// </summary>
public class UserListUI : MonoBehaviour
{
    [SerializeField] private TMP_Text userCountText;
    [SerializeField] private List<TMP_Text> userListSlots;

    private void OnEnable()
    {
        NetworkManager.OnUserCountUpdated += UpdateUserCount;
        NetworkManager.OnUserListReceived += UpdateUserList;
    }

    private void OnDisable()
    {
        NetworkManager.OnUserCountUpdated -= UpdateUserCount;
        NetworkManager.OnUserListReceived -= UpdateUserList;
    }

    /// <summary>
    /// 접속자 수 텍스트 갱신 (예: "2/6")
    /// </summary>
    private void UpdateUserCount(UserCountPacket pkt)
    {
        if (userCountText != null) userCountText.text = pkt.countText;
    }

    /// <summary>
    /// (요구사항 2) 6개의 슬롯에 유저 목록 갱신 (예: "유저1 [2/0]")
    /// </summary>
    private void UpdateUserList(UserListPacket pkt)
    {
        // 6개의 슬롯을 순회
        for (int i = 0; i < userListSlots.Count; i++)
        {
            if (i < pkt.users.Count)
            {
                var user = pkt.users[i];
                userListSlots[i].text = $"{user.Nickname} [{user.Score}]";
                userListSlots[i].gameObject.SetActive(true);
            }
            else
            {
                userListSlots[i].text = "";
                userListSlots[i].gameObject.SetActive(false);
            }
        }
    }
}