
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Linq; 
using System.Collections.Generic; 
using MySqlConnector; 
using Newtonsoft.Json;
using SharedPackets;     

class Program
{
    // ========================================================================
    // [서버 설정]
    // ========================================================================
    
    // 구글 클라우드 서버 포트
    private const int ServerPort = 7777;

    // 최대 접속 인원
    private const int MaxPlayers = 6;

    // DB 연결 문자열
    // API 서버의 appsettings.json에서 사용했던 값과 동일하게 입력 필요
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

        // 클라이언트 접속을 비동기로 계속 대기
        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    // ========================================================================
    // [클라이언트 처리 및 채팅]
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
            // JSON 로그인 패킷 수신 대기
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) return; // 연결 끊김

            string loginJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            LoginRequestPacket? loginReq = null;

            try
            {
                // JSON 파싱 시도
                var tempObj = JsonConvert.DeserializeObject<SharedPackets.BasePacket>(loginJson);
                if (tempObj != null && tempObj.type == "LOGIN_REQ")
                {
                    loginReq = JsonConvert.DeserializeObject<LoginRequestPacket>(loginJson);
                }
            }
            catch
            {
                Console.WriteLine($"[WARN] 잘못된 로그인 패킷 형식: {loginJson}");
            }

            // 패킷 유효성 검사
            if (loginReq == null || string.IsNullOrEmpty(loginReq.nickname))
            {
                var failPkt = new LoginResponsePacket { type = "LOGIN_RES", success = false, message = "잘못된 요청입니다." };
                await SendJsonToClientAsync(client, failPkt);
                client.Close();
                return;
            }

            nickname = loginReq.nickname;

            // 닉네임 길이 등 2차 검증 (서버 측)
            if (nickname.Length > 6) // 한글 6글자 초과 등
            {
                var failPkt = new LoginResponsePacket { type = "LOGIN_RES", success = false, message = "닉네임이 너무 깁니다." };
                await SendJsonToClientAsync(client, failPkt);
                client.Close();
                return;
            }

            // DB 트랜잭션 (로그인/회원가입)
            bool isLoginSuccess = await RegisterOrLoginUserAsync(nickname);

            if (!isLoginSuccess)
            {
                // DB 오류로 실패
                var errorPkt = new LoginResponsePacket { type = "LOGIN_RES", success = false, message = "서버 DB 오류 발생." };
                await SendJsonToClientAsync(client, errorPkt);
                client.Close();
                return;
            }

            // 로그인 응답 전송
            var successPkt = new LoginResponsePacket { type = "LOGIN_RES", success = true, message = "로그인 성공" };
            await SendJsonToClientAsync(client, successPkt);

            // 클라이언트 목록에 정식 등록
            clients.TryAdd(client, nickname);
            Console.WriteLine($"[INFO] '{nickname}' 님이 접속했습니다. (총 {clients.Count}명)");

            // 접속자 수/목록 방송
            await BroadcastUserCountAsync();
            await BroadcastUserListAsync();

            // 환영 메시지 (JSON 귓속말)
            var welcomePkt = new ChatPacket { type = "CHAT", message = $"[서버] '{nickname}'님, 환영합니다.", colorHex = "#00FF00" };
            await SendJsonToClientAsync(client, welcomePkt);

            // 입장 알림 (JSON 방송)
            var enterPkt = new ChatPacket { type = "CHAT", message = $"[서버] '{nickname}' 님이 입장했습니다.", colorHex = "#AAAAAA" };
            await BroadcastJsonAsync(enterPkt, client);

            // 채팅 메시지 수신 루프
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
                    // [수정] 정답자 발생 (JSON 방송)
                    int currentScore = await GetUserScoreAsync(nickname); // 점수 갱신 전 점수 가져오기
                    var winnerPkt = new WinnerPacket
                    {
                        type = "WINNER",
                        winnerName = nickname,
                        answerPokemon = currentQuizAnswer!.SpeciesKorName,
                        newScore = currentScore + 1 // 1점 오른 점수
                    };
                    await BroadcastJsonAsync(winnerPkt, null);

                    // 점수 업데이트
                    await UpdateGameResultAsync(nickname);
                    await BroadcastUserListAsync();

                    // 퀴즈 즉시 종료
                    await StopQuizAsync(null); // 정답자가 있으므로 추가 메시지 없음
                }
                else if (message.Equals("/퀴즈시작", StringComparison.OrdinalIgnoreCase))
                {
                    // 퀴즈 시작 명령어
                    await StartQuizAsync(); // 퀴즈 시작 로직 호출
                }
                else
                {
                    // 일반 채팅 (JSON 방송)
                    // TODO: 유저가 "[HINT]" 같은 태그를 입력하지 못하게 필터링 필요
                    var chatPkt = new ChatPacket { type = "CHAT", message = $"[{nickname}] {message}", colorHex = "#FFFFFF" };
                    await BroadcastJsonAsync(chatPkt, client);
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
            clients.TryRemove(client, out _);
            client.Close();
            _ = BroadcastUserCountAsync();
            _ = BroadcastUserListAsync();

            Console.WriteLine($"[INFO] '{nickname}' 님 퇴장. (남은 {clients.Count}명)");

            // 퇴장 알림 (JSON 방송)
            var exitPkt = new ChatPacket { type = "CHAT", message = $"[서버] '{nickname}' 님이 퇴장했습니다.", colorHex = "#AAAAAA" };
            await BroadcastJsonAsync(exitPkt, null);
        }
    }

    // ========================================================================
    // [퀴즈 시작 및 DB 쿼리]
    // ========================================================================
    /// <summary>
    /// 새 퀴즈를 시작합니다.
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

        var startPkt = new QuizStartPacket { type = "QUIZ_START", message = "[퀴즈] 포켓몬 퀴즈를 시작합니다!" };
        await BroadcastJsonAsync(startPkt, null);

        Pokemon? quiz = await GetRandomPokemonFromDbAsync();

        if (quiz == null)
        {
            // 오류 알림 (JSON 방송)
            var errorPkt = new ChatPacket { type = "CHAT", message = "[오류] DB에서 퀴즈를 가져오는 데 실패했습니다.", colorHex = "#FF0000" };
            await BroadcastJsonAsync(errorPkt, null);
            await StopQuizAsync(null); // (매개변수 추가)
            return;
        }

        // 서버 메모리에 정답과 힌트 목록 저장
        currentQuizAnswer = quiz;
        currentQuizHints = GenerateHintList(quiz); // 힌트 목록 생성

        Console.WriteLine($"[QUIZ] 퀴즈 시작. 정답: {quiz.SpeciesKorName} (ID: {quiz.Id})");
        // (문제를 가져왔습니다 메시지는 QUIZ_START로 대체됨)

        // 첫 힌트 바로 전송
        if (currentQuizHints != null && currentQuizHints.Count > 0)
        {
            var firstHintPkt = new HintPacket { type = "HINT", hintContent = currentQuizHints[0] };
            await BroadcastJsonAsync(firstHintPkt, null);
        }

        //  15초 힌트 타이머는 '두 번째 힌트부터'(.Skip(1)) 시작
        if (currentQuizHints != null && quizTimerCancelToken != null)
        {
            _ = StartHintTimerAsync(currentQuizHints.Skip(1), quizTimerCancelToken.Token);
        }
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

                var command = new MySqlCommand("SELECT * FROM Pokemons ORDER BY RAND() LIMIT 1;", connection);

                await using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        // DB 결과를 Pokemon 객체로 '수동' 매핑
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
    // [힌트 생성 및 타이머]
    // ========================================================================

    /// <summary>
    /// 15초마다 힌트를 하나씩 방송합니다.
    /// </summary>
    private static async Task StartHintTimerAsync(IEnumerable<string> remainingHints, CancellationToken cancelToken)
    {
        if (currentQuizHints == null) return;

        // .Skip(1)로 받은 '나머지 힌트'(4개)를 순회
        foreach (string hint in remainingHints)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancelToken);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[INFO] 힌트 타이머가 정상적으로 취소되었습니다.");
                return;
            }
            var hintPkt = new HintPacket { type = "HINT", hintContent = hint };
            await BroadcastJsonAsync(hintPkt, null);
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

        // 15초가 지났는데도 정답자가 없음 (시간 초과)
        // "CPU"가 이긴 것으로 간주하고 WinnerPacket 방송
        var cpuWinnerPkt = new WinnerPacket
        {
            type = "WINNER",
            winnerName = "CPU",        // 또는 "시스템", "시간초과" 등 원하는 이름
            answerPokemon = currentQuizAnswer!.SpeciesKorName,
            newScore = 0               // CPU 점수는 표시 안 하거나 0으로 처리
        };
        await BroadcastJsonAsync(cpuWinnerPkt, null);

        // 채팅창에도 시간 초과 알림 전송
        var timeoutChatPkt = new ChatPacket
        {
            type = "CHAT",
            message = $"[시스템] 시간 초과! 정답은 '{currentQuizAnswer.SpeciesKorName}'였습니다.",
            colorHex = "#FF0000" // 빨간색
        };
        await BroadcastJsonAsync(timeoutChatPkt, null);

        // 퀴즈 종료 처리
        // (이미 정답 공개를 했으므로 reasonMessage는 null로 보냄)
        await StopQuizAsync(null);
    }

    /// <summary>
    /// 퀴즈를 즉시 종료하고 'QuizEndPacket'을 방송합니다.
    /// </summary>
    private static async Task StopQuizAsync(string? reasonMessage)
    {
        lock (quizLock)
        {
            if (!isQuizActive) return; // 이미 종료됨

            Console.WriteLine("[INFO] 퀴즈 종료 로직 실행.");
            isQuizActive = false;
            currentQuizAnswer = null;
            currentQuizHints = null;

            // 실행 중인 15초 힌트 타이머를 '강제 취소'
            quizTimerCancelToken?.Cancel();
            quizTimerCancelToken = null;
        }

        // 퀴즈 종료 패킷 방송
        var endPkt = new QuizEndPacket
        {
            type = "QUIZ_END",
            // 시간 초과 메시지 또는 정답자가 맞혔다는 메시지 (null일 수 있음)
            reasonMessage = reasonMessage
        };
        await BroadcastJsonAsync(endPkt, null);

        await Task.Delay(1000); // 1초 대기

        // 재시작 안내 (ChatPacket)
        var restartPkt = new ChatPacket { type = "CHAT", message = "[퀴즈] '/퀴즈시작'으로 다시 시작할 수 있습니다.", colorHex = "#AAAAAA" };
        await BroadcastJsonAsync(restartPkt, null);
    }

    /// <summary>
    /// 포켓몬 객체를 받아 5개의 힌트 목록을 생성합니다.
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
    /// 한국어 문자열을 받아 '초성'만 추출합니다. (예: "주리비얀" -> "ㅈㄹㅂㅇ")
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
            // 문자가 '가' ~ '힣' 범위의 한글인지 확인
            if (c >= GAH && c <= HEEH)
            {
                // 유니코드 값을 이용해 초성 인덱스 계산
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
    /// 객체를 JSON으로 직렬화하여 모든 클라이언트에게 방송합니다.
    /// </summary>
    private static async Task BroadcastJsonAsync(BasePacket packet, TcpClient? sender)
    {
        string jsonMessage = JsonConvert.SerializeObject(packet);
        Console.WriteLine($"[BROADCAST_JSON] {jsonMessage}");

        // \n을 메시지 '구분자'로 사용
        byte[] data = Encoding.UTF8.GetBytes(jsonMessage + "\n");

        List<TcpClient> disconnectedClients = new List<TcpClient>();

        // 현재 접속 중인 모든 클라이언트에게 전송
        foreach (var clientEntry in clients)
        {
            TcpClient client = clientEntry.Key;

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
    }

    /// <summary>
    /// 특정 클라이언트 1명에게만 JSON 객체를 보냅니다. (귓속말)
    /// </summary>
    private static async Task SendJsonToClientAsync(TcpClient client, BasePacket packet)
    {
        try
        {
            string jsonMessage = JsonConvert.SerializeObject(packet);
            byte[] data = Encoding.UTF8.GetBytes(jsonMessage + "\n");
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] 귓속말(JSON) 전송 실패: {ex.Message}");
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
                    // Users 테이블에 닉네임 삽입 시도
                    // (만약 이미 존재하는 닉네임이면 여기서 예외가 발생하여 catch로 갑니다 -> 로그인 처리)
                    var insertUserCmd = new MySqlCommand("INSERT INTO Users (Nickname) VALUES (@nickname);", connection, transaction);
                    insertUserCmd.Parameters.AddWithValue("@nickname", nickname);
                    await insertUserCmd.ExecuteNonQueryAsync();

                    // 방금 생성된 유저의 ID 가져오기
                    long newUserId = insertUserCmd.LastInsertedId;

                    // Scoreboard 테이블 초기화 (0승 0패)
                    var insertScoreCmd = new MySqlCommand("INSERT INTO Scoreboard (UserId, Wins, Losses) VALUES (@userId, 0, 0);", connection, transaction);
                    insertScoreCmd.Parameters.AddWithValue("@userId", newUserId);
                    await insertScoreCmd.ExecuteNonQueryAsync();

                    // 모든 작업 성공! 커밋(Commit)하여 진짜로 저장합니다.
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
    // [접속자 목록 & 점수 방송]
    // ========================================================================

    /// <summary>
    /// 모든 유저에게 '현재 접속자 수'를 "JSON"으로 방송합니다.
    /// </summary>
    private static async Task BroadcastUserCountAsync()
    {
        int count = clients.Count;
        var countPkt = new UserCountPacket
        {
            type = "USER_COUNT",
            countText = $"[ {count}/{MaxPlayers} ]"
        };
        await BroadcastJsonAsync(countPkt, null);
    }

    /// <summary>
    /// 모든 유저에게 '접속자 명단'과 '승수'를 "JSON"으로 방송합니다.
    /// </summary>
    private static async Task BroadcastUserListAsync()
    {
        try
        {
            // 현재 접속 중인 모든 닉네임 수집
            var activeNicknames = clients.Values.ToList();
            var userList = new List<UserData>(); // [수정] UserData 객체 리스트

            if (activeNicknames.Count > 0)
            {
                // DB에서 접속 중인 유저들의 점수(Wins) 조회
                using (var connection = new MySqlConnection(DbConnectionString))
                {
                    await connection.OpenAsync();

                    var parameters = new List<string>();
                    for (int i = 0; i < activeNicknames.Count; i++)
                    {
                        parameters.Add($"@nick{i}");
                    }
                    string inClause = string.Join(",", parameters);
                    string sql = $@"
                        SELECT u.Nickname, s.Wins 
                        FROM Scoreboard s
                        JOIN Users u ON s.UserId = u.Id
                        WHERE u.Nickname IN ({inClause});";

                    var cmd = new MySqlCommand(sql, connection);
                    for (int i = 0; i < activeNicknames.Count; i++)
                    {
                        cmd.Parameters.AddWithValue($"@nick{i}", activeNicknames[i]);
                    }

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // UserData 객체로 추가
                            userList.Add(new UserData
                            {
                                Nickname = reader.GetString("Nickname"),
                                Score = reader.GetInt32("Wins")
                            });
                        }
                    }
                }
            }

            // JSON 패킷으로 방송
            var listPkt = new UserListPacket
            {
                type = "USER_LIST",
                users = userList
            };
            await BroadcastJsonAsync(listPkt, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB 오류] 유저 목록 조회 실패: {ex.Message}");
        }
    }

    // ========================================================================
    // [점수 업데이트 트랜잭션]
    // ========================================================================

    /// <summary>
    /// (DB 헬퍼) 닉네임으로 현재 점수를 조회합니다.
    /// </summary>
    private static async Task<int> GetUserScoreAsync(string nickname)
    {
        try
        {
            using (var connection = new MySqlConnection(DbConnectionString))
            {
                await connection.OpenAsync();
                string sql = @"
                    SELECT s.Wins 
                    FROM Scoreboard s
                    JOIN Users u ON s.UserId = u.Id
                    WHERE u.Nickname = @nickname;";
                var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@nickname", nickname);

                object result = await cmd.ExecuteScalarAsync();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB 오류] 점수 조회 실패: {ex.Message}");
        }
        return 0;
    }

    /// <summary>
    /// 퀴즈가 끝났을 때 '승자'의 점수만 트랜잭션으로 안전하게 업데이트합니다.
    /// </summary>
    private static async Task UpdateGameResultAsync(string winnerNickname)
    {
        using (var connection = new MySqlConnection(DbConnectionString))
        {
            await connection.OpenAsync();

            // 트랜잭션 시작
            using (var transaction = await connection.BeginTransactionAsync())
            {
                try
                {
                    // 승자 점수 업데이트 (Wins + 1)
                    string updateWinnerSql = @"
                        UPDATE Scoreboard s 
                        JOIN Users u ON s.UserId = u.Id 
                        SET s.Wins = s.Wins + 1 
                        WHERE u.Nickname = @winnerName;";

                    var winnerCmd = new MySqlCommand(updateWinnerSql, connection, transaction);
                    winnerCmd.Parameters.AddWithValue("@winnerName", winnerNickname);
                    await winnerCmd.ExecuteNonQueryAsync();

                    // 모두 성공하면 커밋
                    await transaction.CommitAsync();
                    Console.WriteLine($"[DB] 점수 업데이트 완료 (승자: {winnerNickname})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB 오류] 점수 갱신 실패, 롤백합니다: {ex.Message}");
                    await transaction.RollbackAsync();
                }
            }
        }
    }
}