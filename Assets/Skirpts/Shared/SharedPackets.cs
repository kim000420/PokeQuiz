// Assets/Scripts/Shared/SharedPackets.cs

using System.Collections.Generic; // List

/// <summary>
/// 서버와 클라이언트가 공용으로 사용할 패킷 정의
/// </summary>
namespace SharedPackets
{
    // 유저 목록 데이터를 전달하기 위한 클래스 (NetworkManager에서 이동)
    public class UserData
    {
        public string Nickname;
        public int Score;
    }

    // --- 기본 패킷 (모든 패킷의 기반) ---
    public class BasePacket
    {
        /// <summary>
        /// 패킷 타입을 식별하기 위한 '헤더'
        /// </summary>
        public string type;
    }

    // --- 서버 -> 클라 ---

    // 일반 채팅/시스템 메시지
    public class ChatPacket : BasePacket
    {
        public string message; // 내용
        public string colorHex; // (확장성) #RRGGBB
    }

    // 유저 수 갱신
    public class UserCountPacket : BasePacket
    {
        public string countText; // "2/6"
    }

    // 유저 목록 갱신
    public class UserListPacket : BasePacket
    {
        public List<UserData> users;
    }

    // 퀴즈 시작 알림
    public class QuizStartPacket : BasePacket
    {
        public string message; // "새 퀴즈를 가져왔습니다..."
    }

    // 힌트 전송
    public class HintPacket : BasePacket
    {
        public string hintContent; // "타입: 불/비행"
    }

    // 정답자 알림
    public class WinnerPacket : BasePacket
    {
        public string winnerName;
        public string answerPokemon;
        public int newScore; // (확장성) 정답자의 새 점수
    }

    // 퀴즈 종료 (시간 초과 또는 정답)
    public class QuizEndPacket : BasePacket
    {
        public string reasonMessage; // (선택) "[시간 초과] 정답은..."
    }
}