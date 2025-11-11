// Assets/Scripts/QuizManager_HttpClient.cs
using UnityEngine;
using System.Net.Http; // .NET의 표준 HTTP 클라이언트
using System.Threading.Tasks; // C# 비동기 통신
using Newtonsoft.Json; // 8-A 단계에서 설치한 JSON 번역기

/// <summary>
/// .NET의 'HttpClient'를 사용해 API 서버와 통신하는 매니저입니다.
/// UnityWebRequest가 알 수 없는 이유로 차단될 때 시도해볼 수 있는 '대체 방안'입니다.
/// </summary>
public class QuizManager_HttpClient : MonoBehaviour
{
    [Header("API 서버 설정")]
    [Tooltip("님의 VM 공용 IP 주소와 포트입니다.")]
    public string serverUrl = "http://34.22.102.159:5065";

    [Header("테스트용")]
    [SerializeField]
    private string _debugPokemonName = "아직 로드 안됨";

    /// <summary>
    /// [중요] HttpClient는 한 번만 생성해서 재사용하는 것이 좋습니다.
    /// </summary>
    private static readonly HttpClient httpClient = new HttpClient();

    /// <summary>
    /// [테스트용] 유니티 에디터의 '재생' 버튼을 누르면 자동으로 퀴즈 1개를 요청합니다.
    /// </summary>
    private async void Start()
    {
        Debug.Log("[HttpClient] 서버에 랜덤 퀴즈를 요청합니다...");
        Pokemon randomPokemon = await GetRandomQuizAsync();

        if (randomPokemon != null)
        {
            _debugPokemonName = randomPokemon.SpeciesKorName;
            Debug.Log($"[HttpClient 성공] 퀴즈 로드 완료: {randomPokemon.SpeciesKorName}");
        }
    }

    /// <summary>
    /// API 서버에 랜덤 포켓몬 퀴즈를 'HttpClient'로 요청하는 함수
    /// </summary>
    public async Task<Pokemon> GetRandomQuizAsync()
    {
        // 1. 요청할 주소를 조합합니다.
        string requestUrl = $"{serverUrl}/api/quiz/random";

        try
        {
            // 2. HttpClient로 GET 요청을 보내고, 응답(문자열)을 비동기로 받습니다.
            string jsonResponse = await httpClient.GetStringAsync(requestUrl);

            // 3. [성공]
            Debug.Log($"[HttpClient 응답] JSON: {jsonResponse}");

            // 4. [핵심] Newtonsoft.Json을 사용해 JSON 문자열을 'Pokemon' 클래스로 자동 변환
            Pokemon pokemon = JsonConvert.DeserializeObject<Pokemon>(jsonResponse);
            return pokemon;
        }
        catch (HttpRequestException e)
        {
            // [실패] 네트워크 레벨의 에러 (Cannot connect 등)
            // 'e.InnerException'에 더 자세한 정보가 있을 수 있습니다.
            Debug.LogError($"[HttpClient 에러] {requestUrl} 요청 실패: {e.Message}");
            if (e.InnerException != null)
            {
                Debug.LogError($"[HttpClient 상세 에러] {e.InnerException.Message}");
            }
            return null;
        }
        catch (System.Exception e)
        {
            // 기타 모든 에러 (JSON 파싱 실패 등)
            Debug.LogError($"[HttpClient 시스템 에러] {e.Message}");
            return null;
        }
    }
}