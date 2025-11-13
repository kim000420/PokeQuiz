
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent; // 스레드 안전한 딕셔너리
using System.Threading.Tasks;
using System.Threading; // CancellationTokenSource (타이머 취소용)
using System.Linq; // Random.Shared
using System.Collections.Generic; // List
using MySqlConnector; // 9-A 단계에서 설치한 MySQL 드라이버

// 9-B 단계에서 만든 Pokemon 모델 (파일이 같은 폴더에 있으므로 네임스페이스 불필요)
// using PokemonChatServer.Models; 

class Program
{
    // ========================================================================
    // [서버 설정]
    // ========================================================================
    
    // 구글 클라우드 서버 포트
    private const int ServerPort = 7777;

    // DB 연결 문자열
    // 이전에 API 서버의 appsettings.json에서 사용했던 값과 동일하게 입력 필요
    private const string DbConnectionString = "server=localhost;port=3306;database=pokemon_db;user=root;password=PkM!api#2025";

    // ========================================================================
    // [서버 관리 변수]
    // ========================================================================

    // 접속한 클라이언트 목록
    // Key: TcpClient (소켓)
    // Value: string (유저 닉네임)
    private static readonly ConcurrentDictionary<TcpClient, string> clients = new ConcurrentDictionary<TcpClient, string>();

    // 퀴즈 상태를 관리하는 변수들
    private static readonly object quizLock = new object(); // 퀴즈 시작/종료 시 동시 접근 방지용
    private static bool isQuizActive = false; // 퀴즈가 현재 진행 중인지?
    private static Pokemon? currentQuizAnswer = null; // 현재 퀴즈의 정답 포켓몬 객체
    private static List<string>? currentQuizHints = null; // 현재 퀴즈의 힌트 목록
    private static CancellationTokenSource? quizTimerCancelToken; // 힌트 타이머를 '취소'하기 위한 토큰

    // ========================================================================
    // [메인: 서버 시작]
    // ========================================================================
    static async Task Main(string[] args)
    {
        TcpListener server = new TcpListener(IPAddress.Any, ServerPort);
        server.Start();
        Console.WriteLine($"[INFO] 포켓몬 퀴즈 서버가 포트 {ServerPort}에서 시작되었습니다...");
        Console.WriteLine($"[INFO] DB 연결 대상: {DbConnectionString.Substring(0, DbConnectionString.IndexOf("password="))}...");

        // (기능 2) 클라이언트 접속을 비동기로 계속 대기
        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();

            // 클라이언트가 접속하면, HandleClientAsync 메서드를 '새 스레드'에서 실행
            // (await를 붙이지 않아야 다음 클라이언트를 바로 받을 수 있음)
            _ = HandleClientAsync(client);
        }
    }

    // ========================================================================
    // [기능 1, 2, 3, 7: 클라이언트 처리 및 채팅]
    // ========================================================================
    /// <summary>
    /// 개별 클라이언트의 메시지 수신 및 처리를 담당합니다.
    /// </summary>
    private static async Task HandleClientAsync(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        string nickname = string.Empty;

        try
        {
            // (기능 1) 닉네임 로그인: 클라이언트의 '첫 번째' 메시지를 닉네임으로 간주
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) return; // 연결 직후 끊김

            nickname = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            // 닉네임이 유효한지 간단히 확인
            if (string.IsNullOrEmpty(nickname) || nickname.Length > 12)
            {
                nickname = $"User{Random.Shared.Next(100, 999)}";
            }

            // DB 트랜잭션을 통한 회원가입/로그인 시도
            bool isLoginSuccess = await RegisterOrLoginUserAsync(nickname);
            if (!isLoginSuccess)
            {
                // DB 오류 시 접속 거부
                await SendMessageToClientAsync(client, "[오류] 서버 DB 문제로 접속할 수 없습니다.");
                client.Close();
                return;
            }

            // 클라이언트 목록에 정식 등록
            clients.TryAdd(client, nickname);
            Console.WriteLine($"[INFO] '{nickname}' 님이 접속했습니다. (총 {clients.Count}명)");

            // [추가됨] 접속자 수 방송 & 내 점수 전송
            await BroadcastUserCountAsync();
            await SendMyScoreAsync(client, nickname); // 로그인하자마자 내 점수 갱신

            // (기능 2) 채팅 서버 입장 완료: 본인에게 환영 메시지 전송
            await SendMessageToClientAsync(client, $"[서버] '{nickname}'님, 환영합니다. '/퀴즈시작'을 입력해 퀴즈를 시작하세요.");

            // (기능 2) 채팅 서버 입장 완료: 다른 모두에게 입장 알림
            await BroadcastMessageAsync($"[서버] '{nickname}' 님이 입장했습니다.", client);

            // (기능 2, 3, 7) 채팅 메시지 수신 루프
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                if (string.IsNullOrEmpty(message)) continue;

                Console.WriteLine($"[CHAT] {nickname}: {message}");

                // --- 퀴즈 로직 검사 ---
                bool isAnswer = false;
                lock (quizLock)
                {
                    // 정답 판정: 퀴즈가 진행 중이고, 메시지가 정답과 일치하는가?
                    if (isQuizActive && currentQuizAnswer != null &&
                        message.Equals(currentQuizAnswer.SpeciesKorName, StringComparison.OrdinalIgnoreCase))
                    {
                        isAnswer = true;
                        // 정답을 맞혔으므로 즉시 퀴즈 종료 로직 호출
                        // (BroadcastMessageAsync는 lock 바깥에서 호출해야 데드락이 안 걸림)
                    }
                }

                if (isAnswer)
                {
                    // 정답자 발생
                    await BroadcastMessageAsync($"[정답!] '{nickname}' 님이 정답 '{currentQuizAnswer!.SpeciesKorName}'을(를) 맞혔습니다!", null);

                    // 점수 업데이트 트랜잭션 실행
                    var currentPlayers = clients.Values.ToList();
                    await UpdateGameResultAsync(nickname, currentPlayers);

                    // 승리한 유저에게 '갱신된 점수' 다시 전송
                    await SendMyScoreAsync(client, nickname);
                    
                    // 퀴즈 즉시 종료
                    await StopQuizAsync(); 
                }
                else if (message.Equals("/퀴즈시작", StringComparison.OrdinalIgnoreCase))
                {
                    // 퀴즈 시작 명령어
                    await StartQuizAsync(); // 퀴즈 시작 로직 호출
                }
                else
                {
                    // 일반 채팅
                    await BroadcastMessageAsync($"[{nickname}] {message}", client);
                }
            }
        }
        catch (Exception ex)
        {
            // 네트워크 오류 또는 클라이언트 연결 끊김
            Console.WriteLine($"[WARN] '{nickname}' 님 접속 종료 또는 오류: {ex.Message}");
        }
        finally
        {
            // 클라이언트 목록에서 제거
            clients.TryRemove(client, out _);
            client.Close();

            // 접속자 수 갱신 방송
            _ = BroadcastUserCountAsync();

            Console.WriteLine($"[INFO] '{nickname}' 님 퇴장. (남은 {clients.Count}명)");
            await BroadcastMessageAsync($"[서버] '{nickname}' 님이 퇴장했습니다.", null);
        }
    }

    // ========================================================================
    // [기능 3, 4: 퀴즈 시작 및 DB 쿼리]
    // ========================================================================
    /// <summary>
    /// 새 퀴즈를 시작합니다. (기능 3, 4)
    /// </summary>
    private static async Task StartQuizAsync()
    {
        lock (quizLock)
        {
            if (isQuizActive)
            {
                // TODO: 퀴즈 시작을 요청한 사람에게만 "이미 진행 중"이라고 귓속말
                Console.WriteLine("[WARN] 이미 퀴즈가 진행 중이나, '/퀴즈시작' 요청이 또 들어옴.");
                return;
            }
            isQuizActive = true; // 퀴즈 상태를 '진행 중'으로 변경
            currentQuizAnswer = null; // 이전 정답 초기화
            currentQuizHints = null; // 이전 힌트 초기화
            quizTimerCancelToken = new CancellationTokenSource(); // 새 타이머 '취소 토큰' 생성
        }

        await BroadcastMessageAsync("[퀴즈] 포켓몬 퀴즈를 시작합니다!", null);

        // (기능 4) DB에서 랜덤 포켓몬 1마리 가져오기
        Pokemon? quiz = await GetRandomPokemonFromDbAsync();

        if (quiz == null)
        {
            await BroadcastMessageAsync("[오류] DB에서 퀴즈를 가져오는 데 실패했습니다. 퀴즈를 종료합니다.", null);
            await StopQuizAsync();
            return;
        }

        // [핵심] 서버 메모리에 정답과 힌트 목록 저장
        currentQuizAnswer = quiz;
        currentQuizHints = GenerateHintList(quiz); // (기능 6) 힌트 목록 생성

        Console.WriteLine($"[QUIZ] 퀴즈 시작. 정답: {quiz.SpeciesKorName} (ID: {quiz.Id})");
        await BroadcastMessageAsync("[퀴즈] 문제를 가져왔습니다! 15초 후 첫 번째 힌트가 나갑니다.", null);

        // (기능 5) 15초 힌트 타이머 시작
        // (await를 붙이지 않아야 다른 채팅을 계속 처리할 수 있음)
        if (currentQuizHints != null && currentQuizHints.Count > 0)
        {
            await BroadcastMessageAsync($"[힌트] {currentQuizHints[0]}", null);
        }

        //  15초 힌트 타이머는 '두 번째 힌트부터'(.Skip(1)) 시작
        _ = StartHintTimerAsync(currentQuizHints.Skip(1), quizTimerCancelToken.Token);
    }

    /// <summary>
    /// [핵심 DB 쿼리] MySQL DB에 연결해 랜덤 포켓몬 1마리를 가져옵니다. (기능 4)
    /// </summary>
    private static async Task<Pokemon?> GetRandomPokemonFromDbAsync()
    {
        try
        {
            await using (var connection = new MySqlConnection(DbConnectionString))
            {
                await connection.OpenAsync(); // DB 연결

                // [쿼리] 님이 원하셨던 'DB 쿼리 경험'의 핵심입니다.
                var command = new MySqlCommand("SELECT * FROM Pokemons ORDER BY RAND() LIMIT 1;", connection);

                await using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        // DB 결과를 Pokemon 객체로 '수동' 매핑
                        // (MySqlConnector 에는 Dapper 같은 자동 매핑 기능이 없으므로,
                        //  컬럼 이름을 정확히 알고 있어야 합니다.)
                        return new Pokemon
                        {
                            Id = reader.GetInt32("Id"),
                            DexId = reader.GetInt32("DexId"),
                            SpeciesEngName = reader.GetString("SpeciesEngName"),
                            SpeciesKorName = reader.GetString("SpeciesKorName"),
                            FormId = reader.GetInt32("FormId"),
                            FormEngName = reader.GetString("FormEngName"),
                            FormKey = reader.GetString("FormKey"),
                            TypeA = reader.GetString("TypeA"),
                            TypeB = reader.IsDBNull(reader.GetOrdinal("TypeB")) ? null : reader.GetString("TypeB"),
                            Generation = reader.GetInt32("Generation"),
                            GenderUnknown = reader.GetBoolean("GenderUnknown"),
                            GenderMale = reader.GetFloat("GenderMale"),
                            GenderFemale = reader.GetFloat("GenderFemale"),
                            EggSteps = reader.GetInt32("EggSteps"),
                            EggGroup1 = reader.GetString("EggGroup1"),
                            EggGroup2 = reader.IsDBNull(reader.GetOrdinal("EggGroup2")) ? null : reader.GetString("EggGroup2"),
                            CatchRate = reader.GetInt32("CatchRate"),
                            ExperienceGroup = reader.GetString("ExperienceGroup"),
                            RarityCategory = reader.GetString("RarityCategory"),
                            H = reader.GetInt32("H"),
                            A = reader.GetInt32("A"),
                            B = reader.GetInt32("B"),
                            C = reader.GetInt32("C"),
                            D = reader.GetInt32("D"),
                            S = reader.GetInt32("S"),
                            Total = reader.GetInt32("Total")
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB 오류] 쿼리 실행 실패: {ex.Message}");
        }
        return null; // 실패
    }


    // ========================================================================
    // [기능 5, 6: 힌트 생성 및 타이머]
    // ========================================================================

    /// <summary>
    /// 15초마다 힌트를 하나씩 방송합니다. (기능 5)
    /// </summary>
    private static async Task StartHintTimerAsync(IEnumerable<string> remainingHints, CancellationToken cancelToken)
    {
        if (currentQuizHints == null) return;

        // .Skip(1)로 받은 '나머지 힌트'(4개)를 순회
        foreach (string hint in remainingHints)
        {
            try
            {
                // 1. 15초 대기
                await Task.Delay(TimeSpan.FromSeconds(15), cancelToken);
            }
            catch (TaskCanceledException)
            {
                // [정답!] 누군가 정답을 맞혀서 타이머가 '취소'됨
                Console.WriteLine("[INFO] 힌트 타이머가 정상적으로 취소되었습니다.");
                return;
            }

            // 2. 15초가 지났으므로 다음 힌트 방송 (2~5번 힌트)
            await BroadcastMessageAsync($"[힌트] {hint}", null);
        }

        // 모든 힌트(초성 포함)가 나간 후, 정답을 맞힐 '마지막 15초'를 기다립니다.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancelToken);
        }
        catch (TaskCanceledException)
        {
            // 마지막 15초 안에 정답을 맞힘
            Console.WriteLine("[INFO] (최종) 힌트 타이머가 정상적으로 취소되었습니다.");
            return;
        }

        // [수정됨 - 요구사항 3]
        // 15초가 지났는데도 정답자가 없음 (시간 초과)
        await BroadcastMessageAsync($"[시간 초과] 정답은 '{currentQuizAnswer?.SpeciesKorName}'였습니다!", null);
        await StopQuizAsync(); // 퀴즈 자동 종료
    }

    /// <summary>
    /// 퀴즈를 즉시 종료합니다. (정답을 맞혔거나, 시간 초과 시)
    /// </summary>
    private static async Task StopQuizAsync()
    {
        lock (quizLock)
        {
            if (!isQuizActive) return; // 이미 종료됨

            Console.WriteLine("[INFO] 퀴즈 종료 로직 실행.");
            isQuizActive = false;
            currentQuizAnswer = null;
            currentQuizHints = null;

            // (기능 5) 실행 중인 15초 힌트 타이머를 '강제 취소'
            quizTimerCancelToken?.Cancel();
            quizTimerCancelToken = null;
        }
        await Task.Delay(1000); // 1초 대기
        await BroadcastMessageAsync("[퀴즈] 퀴즈가 종료되었습니다.\n[퀴즈] '/퀴즈시작'으로 다시 시작할 수 있습니다.", null);
    }

    /// <summary>
    /// 포켓몬 객체를 받아 5개의 힌트 목록을 생성합니다. (기능 6)
    /// </summary>
    private static List<string> GenerateHintList(Pokemon quiz)
    {
        var finalHintList = new List<string>(5);

        // 1번 힌트는 '타입 A/B'로 고정
        string typeHint = $"[ 타입 ]\n{quiz.TypeA} / {quiz.TypeB ?? "단일"}";
        finalHintList.Add(typeHint);

        // 나머지 힌트 풀 (6개) 생성
        var hintPool = new List<string>
        {
            $"[ 도감 번호 ]\n{quiz.DexId}",
            $"[ 등장 세대 ]\n{quiz.Generation}세대",
            $"[ 레어도 ]\n{quiz.RarityCategory}",
            $"[ 총합 종족값 ]\n{quiz.Total}",
            quiz.GenderUnknown ? "[ 성별 ]\n없음(무성)" : $"[ 성비(남/여) ]\n{quiz.GenderMale}% / {quiz.GenderFemale}%",
            $"[ 영어 이름 ]\n{quiz.FormEngName}"
        };

        // 6개 중 3개를 '랜덤으로' 섞어서 2, 3, 4번 힌트로 추가
        var randomHints = hintPool.OrderBy(h => Random.Shared.Next()).Take(3).ToList();
        finalHintList.AddRange(randomHints);

        // 5번 힌트는 '초성'으로 고정
        string choseongHint = GetChoseong(quiz.SpeciesKorName);
        finalHintList.Add($"[ 초성 힌트 ]\n{choseongHint}"); // 5번째 힌트로 추가

        return finalHintList;
    }

    /// <summary>
    /// (기능 6) 한국어 문자열을 받아 '초성'만 추출합니다. (예: "주리비얀" -> "ㅈㄹㅂㅇ")
    /// </summary>
    private static string GetChoseong(string koreanText)
    {
        if (string.IsNullOrEmpty(koreanText)) return "";

        // 유니코드 '가' ~ '힣' 범위의 시작과 끝
        const int GAH = 44032;
        const int HEEH = 55203;

        // 초성 19개 배열 (순서 중요)
        char[] choseongList = { 'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ' };

        StringBuilder sb = new StringBuilder();
        foreach (char c in koreanText)
        {
            // 1. 문자가 '가' ~ '힣' 범위의 한글인지 확인
            if (c >= GAH && c <= HEEH)
            {
                // 2. 유니코드 값을 이용해 초성 인덱스 계산
                int choseongIndex = (c - GAH) / (21 * 28);
                sb.Append(choseongList[choseongIndex]);
            }
            else
            {
                // 한글이 아니면(영어, 숫자, 공백) 그대로 추가
                sb.Append(c);
            }
        }
        return sb.ToString();
    }


    // ========================================================================
    // [헬퍼: 메시지 전송]
    // ========================================================================

    /// <summary>
    /// 모든 클라이언트에게 메시지를 방송(Broadcast)합니다.
    /// 'sender'가 null이 아니면, 그 클라이언트는 제외합니다 (선택).
    /// </summary>
    private static async Task BroadcastMessageAsync(string message, TcpClient? sender)
    {
        Console.WriteLine($"[BROADCAST] {message}"); // 서버 로그에도 기록
        byte[] data = Encoding.UTF8.GetBytes(message + "\n"); // (Unity 클라이언트를 위해 개행 문자 추가)

        List<TcpClient> disconnectedClients = new List<TcpClient>();

        // 현재 접속 중인 모든 클라이언트에게 전송
        foreach (var clientEntry in clients)
        {
            TcpClient client = clientEntry.Key;

            // 메시지를 보낸 사람(sender)에게는 다시 보내지 않음 (선택 사항)
            // (지금은 정답자도 정답 메시지를 봐야 하므로 이 코드는 주석 처리)
            // if (client == sender) continue;

            try
            {
                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception)
            {
                // 전송 실패 (연결이 끊어진 클라이언트)
                disconnectedClients.Add(client);
            }
        }

        // 목록에서 연결 끊긴 클라이언트들 정리 (나중에)
        // (실제 프로덕션에서는 이 부분을 더 견고하게 처리해야 합니다.)
        // foreach (var client in disconnectedClients)
        // {
        //     clients.TryRemove(client, out _);
        // }
    }

    /// <summary>
    /// 특정 클라이언트 1명에게만 메시지를 보냅니다. (귓속말)
    /// </summary>
    private static async Task SendMessageToClientAsync(TcpClient client, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message + "\n");
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] 귓속말 전송 실패: {ex.Message}");
        }
    }

    // ========================================================================
    // [로그인 및 트랜잭션]
    // ========================================================================

    /// <summary>
    /// [핵심] 트랜잭션을 사용하여 신규 유저를 등록하거나, 기존 유저로 로그인합니다.
    /// </summary>
    private static async Task<bool> RegisterOrLoginUserAsync(string nickname)
    {
        using (var connection = new MySqlConnection(DbConnectionString))
        {
            await connection.OpenAsync();

            // 1. 트랜잭션 시작 (이 시점부터는 '성공' 아니면 '없던 일'입니다)
            using (var transaction = await connection.BeginTransactionAsync())
            {
                try
                {
                    // [Step 1] Users 테이블에 닉네임 삽입 시도
                    // (만약 이미 존재하는 닉네임이면 여기서 예외가 발생하여 catch로 갑니다 -> 로그인 처리)
                    var insertUserCmd = new MySqlCommand("INSERT INTO Users (Nickname) VALUES (@nickname);", connection, transaction);
                    insertUserCmd.Parameters.AddWithValue("@nickname", nickname);
                    await insertUserCmd.ExecuteNonQueryAsync();

                    // [Step 2] 방금 생성된 유저의 ID 가져오기
                    long newUserId = insertUserCmd.LastInsertedId;

                    // [Step 3] Scoreboard 테이블 초기화 (0승 0패)
                    var insertScoreCmd = new MySqlCommand("INSERT INTO Scoreboard (UserId, Wins, Losses) VALUES (@userId, 0, 0);", connection, transaction);
                    insertScoreCmd.Parameters.AddWithValue("@userId", newUserId);
                    await insertScoreCmd.ExecuteNonQueryAsync();

                    // [Step 4] 모든 작업 성공! 커밋(Commit)하여 진짜로 저장합니다.
                    await transaction.CommitAsync();
                    Console.WriteLine($"[DB] 신규 유저 '{nickname}' 등록 완료 (트랜잭션 성공)");
                    return true; // 신규 등록 성공
                }
                catch (MySqlException ex)
                {
                    // 오류 번호 1062: Duplicate entry (중복된 닉네임)
                    if (ex.Number == 1062)
                    {
                        // 이미 있는 유저이므로, 롤백할 필요 없이 그냥 '로그인'으로 처리
                        // (트랜잭션은 자동으로 롤백됩니다)
                        Console.WriteLine($"[DB] 기존 유저 '{nickname}' 로그인 성공.");
                        return true; // 로그인 성공
                    }
                    else
                    {
                        // 진짜 DB 오류인 경우
                        Console.WriteLine($"[DB 오류] 트랜잭션 실패, 롤백합니다: {ex.Message}");
                        await transaction.RollbackAsync(); // [중요] 롤백! (Users에 들어간 데이터도 취소됨)
                        return false; // 실패
                    }
                }
            }
        }
    }

    // ========================================================================
    // [접속자 수 & 점수 전송]
    // ========================================================================

    /// <summary>
    /// 모든 유저에게 '현재 접속자 수'를 방송합니다. (태그: [USER_COUNT])
    /// </summary>
    private static async Task BroadcastUserCountAsync()
    {
        int count = clients.Count;
        // 예: "[USER_COUNT] 5"
        await BroadcastMessageAsync($"[USER_COUNT] {count}", null);
    }

    /// <summary>
    /// 특정 유저에게 '자신의 승리 횟수(점수)'를 DB에서 조회하여 보냅니다. (태그: [MY_SCORE])
    /// </summary>
    private static async Task SendMyScoreAsync(TcpClient client, string nickname)
    {
        try
        {
            using (var connection = new MySqlConnection(DbConnectionString))
            {
                await connection.OpenAsync();
                // Users 테이블과 조인하여 해당 닉네임의 Wins(승리 수)를 가져옴
                string sql = @"
                    SELECT s.Wins FROM Scoreboard s
                    JOIN Users u ON s.UserId = u.Id
                    WHERE u.Nickname = @nickname;";

                var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@nickname", nickname);

                object? result = await cmd.ExecuteScalarAsync();
                int wins = result != null ? Convert.ToInt32(result) : 0;

                // 예: "[MY_SCORE] 10"
                await SendMessageToClientAsync(client, $"[MY_SCORE] {wins}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB 오류] 점수 조회 실패({nickname}): {ex.Message}");
        }
    }
}