// Assets/Scripts/QuizManager.cs
using UnityEngine;
using UnityEngine.Networking; // Unity의 웹 통신 기능을 사용
using System.Threading.Tasks; // C# 비동기 통신 (Async)
using Newtonsoft.Json; // 8-A 단계에서 설치한 JSON 번역기

/// <summary>
/// .NET API 서버와 통신하여 퀴즈 데이터를 가져오는 매니저입니다.
/// </summary>
public class QuizManager : MonoBehaviour
{
    [Header("API 서버 설정")]
    [Tooltip("님의 VM 공용 IP 주소와 포트입니다.")]
    // [중요!] 님의 .NET 서버는 5065 포트를 쓰므로 http:// 입니다.
    // 만약 7160 같은 https 포트를 쓰게 되면 https:// 로 바꿔야 합니다.
    public string serverUrl = "http://34.22.102.159:5065";

    [Header("테스트용")]
    [Tooltip("API 호출로 받아온 포켓몬의 이름")]
    [SerializeField]
    private string _debugPokemonName = "아직 로드 안됨";

    /// <summary>
    /// [테스트용] 유니티 에디터의 '재생' 버튼을 누르면 자동으로 퀴즈 1개를 요청합니다.
    /// </summary>
    private async void Start()
    {
        Debug.Log("서버에 랜덤 퀴즈를 요청합니다...");
        Pokemon randomPokemon = await GetRandomQuizAsync();

        if (randomPokemon != null)
        {
            _debugPokemonName = randomPokemon.SpeciesKorName;
            Debug.Log($"[성공] 퀴즈 로드 완료: {randomPokemon.SpeciesKorName} (타입1: {randomPokemon.TypeA})");
        }
    }

    /// <summary>
    /// API 서버에 랜덤 포켓몬 퀴즈를 비동기로 요청하는 메인 함수
    /// </summary>
    public async Task<Pokemon> GetRandomQuizAsync()
    {
        // 1. 요청할 주소를 조합합니다. (예: http://...:5065/api/quiz/random)
        string requestUrl = $"{serverUrl}/api/quiz/random";

        // 2. UnityWebRequest를 생성합니다. (GET 요청)
        using (UnityWebRequest webRequest = UnityWebRequest.Get(requestUrl))
        {
            // 3. [헤더] 기획자 편의: 이 요청이 JSON을 원한다고 서버에 명시
            webRequest.SetRequestHeader("Accept", "application/json");

            // 4. API 서버에 요청을 보내고 응답을 기다립니다. (비동기)
            var operation = webRequest.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield(); // 응답이 올 때까지 매 프레임 대기
            }

            // 5. 응답 결과 처리
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                // [성공]
                string jsonResponse = webRequest.downloadHandler.text;
                Debug.Log($"[서버 응답] JSON: {jsonResponse}");

                // [핵심] Newtonsoft.Json을 사용해 JSON 문자열을 'Pokemon' 클래스로 자동 변환
                Pokemon pokemon = JsonConvert.DeserializeObject<Pokemon>(jsonResponse);
                return pokemon;
            }
            else
            {
                // [실패]
                Debug.LogError($"[API 에러] {requestUrl} 요청 실패: {webRequest.error}");
                Debug.LogError($"[서버 에러 메시지] {webRequest.downloadHandler.text}");
                return null;
            }
        }
    }

    // TODO: (미래 9단계) Photon 채팅 서버 연결 로직
    // TODO: (미래 10단계) 정답 확인 로직
}