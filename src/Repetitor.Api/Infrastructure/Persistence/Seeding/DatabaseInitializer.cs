using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repetitor.Api.Configuration;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Infrastructure.Persistence.Seeding;

public sealed class DatabaseInitializer(
    IDbContextFactory<AppDbContext> dbFactory,
    SchemaPatches patches,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>
    /// Выполняет полную инициализацию базы данных при запуске приложения.
    /// Применяет миграции, патчи схемы и заполняет БД стартовыми данными (языки, курсы, грамматика, словарь, упражнения, администратор).
    /// Идемпотентна: при повторном запуске существующие данные не дублируются.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.Database.MigrateAsync(ct);
        }

        await patches.ApplyAsync(ct);
        await SeedLanguagesAsync(ct);
        await SeedCoursesAsync(ct);
        await SeedGrammarAsync(ct);
        await SeedDictionaryAsync(ct);
        await SeedDemoExercisesAsync(ct);

        if (configuration.GetValue("Seed:AdminEmail", string.Empty) is { Length: > 0 } email)
        {
            await SeedAdminAsync(email, configuration["Seed:AdminPassword"] ?? "ChangeMe123!", ct);
        }
    }

    private async Task SeedLanguagesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Languages.AnyAsync(ct))
        {
            return;
        }

        db.Languages.AddRange(
            Lang("ru", "Russian", "Русский", "русский", "🇷🇺", 0),
            Lang("en", "English", "Английский", "English", "🇬🇧", 1),
            Lang("de", "German", "Немецкий", "Deutsch", "🇩🇪", 2),
            Lang("fr", "French", "Французский", "français", "🇫🇷", 3),
            Lang("es", "Spanish", "Испанский", "español", "🇪🇸", 4),
            Lang("it", "Italian", "Итальянский", "italiano", "🇮🇹", 5),
            Lang("pt", "Portuguese", "Португальский", "português", "🇵🇹", 6),
            Lang("pl", "Polish", "Польский", "polski", "🇵🇱", 7),
            Lang("tr", "Turkish", "Турецкий", "Türkçe", "🇹🇷", 8),
            Lang("uk", "Ukrainian", "Украинский", "українська", "🇺🇦", 9),
            Lang("zh", "Chinese", "Китайский", "中文", "🇨🇳", 10),
            Lang("ja", "Japanese", "Японский", "日本語", "🇯🇵", 11),
            Lang("ko", "Korean", "Корейский", "한국어", "🇰🇷", 12),
            Lang("ar", "Arabic", "Арабский", "العربية", "🇸🇦", 13),
            Lang("cs", "Czech", "Чешский", "čeština", "🇨🇿", 14),
            Lang("nl", "Dutch", "Нидерландский", "Nederlands", "🇳🇱", 15),
            Lang("sv", "Swedish", "Шведский", "svenska", "🇸🇪", 16),
            Lang("no", "Norwegian", "Норвежский", "norsk", "🇳🇴", 17),
            Lang("fi", "Finnish", "Финский", "suomi", "🇫🇮", 18),
            Lang("da", "Danish", "Датский", "dansk", "🇩🇰", 19),
            Lang("el", "Greek", "Греческий", "ελληνικά", "🇬🇷", 20),
            Lang("he", "Hebrew", "Иврит", "עברית", "🇮🇱", 21),
            Lang("hi", "Hindi", "Хинди", "हिन्दी", "🇮🇳", 22),
            Lang("id", "Indonesian", "Индонезийский", "Bahasa Indonesia", "🇮🇩", 23),
            Lang("ro", "Romanian", "Румынский", "română", "🇷🇴", 24),
            Lang("vi", "Vietnamese", "Вьетнамский", "Tiếng Việt", "🇻🇳", 25));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} languages", await db.Languages.CountAsync(ct));
    }

    private static Language Lang(string code, string en, string ru, string native, string flag, int order) => new()
    {
        Code = code,
        NameEnglish = en,
        NameRussian = ru,
        NativeName = native,
        FlagEmoji = flag,
        SortOrder = order
    };

    private async Task SeedCoursesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Courses.AnyAsync(ct))
        {
            return;
        }

        var languages = await db.Languages.ToDictionaryAsync(l => l.Code, ct);
        var plan = new (string Code, string[] Levels, string[] Titles)[]
        {
            ("en", ["A1", "A2", "B1", "B2", "C1"],
                ["English Starter", "Everyday English", "English Conversation", "Business English", "Academic English"]),
            ("de", ["A1", "B1", "B2"],
                ["Deutsch für Anfänger", "Deutsch im Alltag", "Deutsch für die Arbeit"]),
            ("es", ["A1", "A2", "B1"],
                ["Español básico", "Español conversacional", "Español intermedio"]),
            ("fr", ["A1", "B1"],
                ["Français débutant", "Français courant"]),
            ("it", ["A1", "A2"],
                ["Italiano base", "Italiano pratico"]),
            ("ja", ["A1", "A2"],
                ["日本語 初級", "日本語 日常会話"]),
            ("zh", ["A1", "A2"],
                ["中文 入门", "中文 日常"])
        };

        var order = 0;
        foreach (var (code, levels, titles) in plan)
        {
            if (!languages.TryGetValue(code, out var language))
            {
                continue;
            }

            for (var i = 0; i < levels.Length; i++)
            {
                var level = Enum.Parse<CefrLevel>(levels[i]);
                var course = new Course
                {
                    LanguageId = language.Id,
                    Slug = $"{code}-{levels[i].ToLowerInvariant()}",
                    Title = titles[i],
                    Description = $"Курс уровня {levels[i]}: грамматика, лексика и практика.",
                    Level = level,
                    EstimatedMinutes = (i + 1) * 120,
                    AccentColor = AccentFor(code),
                    SortOrder = order++
                };
                db.Courses.Add(course);

                for (var l = 1; l <= 5; l++)
                {
                    db.Lessons.Add(new Lesson
                    {
                        CourseId = course.Id,
                        Slug = $"{code}-{levels[i].ToLowerInvariant()}-lesson-{l}",
                        Title = $"Урок {l}: {LessonTitle(l)}",
                        Summary = $"Тема урока: {LessonTitle(l).ToLowerInvariant()}. 15–20 минут практики.",
                        ContentMarkdown = BuildLessonMarkdown(language.NameEnglish, levels[i], LessonTitle(l)),
                        SortOrder = l,
                        EstimatedMinutes = 15 + l * 2,
                        KeyVocabulary = StarterVocabulary(l)
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} courses and {Count} lessons",
            await db.Courses.CountAsync(ct), await db.Lessons.CountAsync(ct));
    }

    private async Task SeedGrammarAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.GrammarTopics.AnyAsync(ct))
        {
            return;
        }

        var languages = await db.Languages.ToDictionaryAsync(l => l.Code, ct);
        var topics = new (string Lang, CefrLevel, string Slug, string Title)[]
        {
            ("en", CefrLevel.A1, "en-articles", "Артикли a / an / the"),
            ("en", CefrLevel.A1, "en-present-simple", "Present Simple"),
            ("en", CefrLevel.A2, "en-past-simple", "Past Simple"),
            ("en", CefrLevel.A2, "en-present-perfect", "Present Perfect"),
            ("en", CefrLevel.B1, "en-conditional", "Условные предложения"),
            ("en", CefrLevel.B1, "en-passive", "Пассивный залог"),
            ("en", CefrLevel.B2, "en-reported-speech", "Косвенная речь"),
            ("de", CefrLevel.A1, "de-articles", "Артикли der / die / das"),
            ("de", CefrLevel.A1, "de-cases", "Падежи"),
            ("de", CefrLevel.B1, "de-word-order", "Порядок слов"),
            ("es", CefrLevel.A1, "es-ser-vs-estar", "ser vs estar"),
            ("es", CefrLevel.A2, "es-preterite", "Pretérito indefinido"),
            ("fr", CefrLevel.A1, "fr-articles", "Артикли"),
            ("fr", CefrLevel.A1, "fr-etre-avoir", "Глаголы être и avoir"),
            ("it", CefrLevel.A1, "it-articles", "Артикли"),
            ("ja", CefrLevel.A1, "ja-hiragana", "Хирагана"),
            ("zh", CefrLevel.A1, "zh-toned-pinyin", "Тональный пиньинь")
        };

        foreach (var (code, level, slug, title) in topics)
        {
            if (!languages.TryGetValue(code, out var language))
            {
                continue;
            }

            db.GrammarTopics.Add(new GrammarTopic
            {
                LanguageId = language.Id,
                Slug = slug,
                Title = title,
                Summary = $"Краткая теория: {title}.",
                ExplanationMarkdown = $"## {title}\n\n**Уровень:** {level}\n\nДобавьте примеры и упражнения через ИИ-генератор.\n",
                MinLevel = level
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} grammar topics", await db.GrammarTopics.CountAsync(ct));
    }

    private async Task SeedDictionaryAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.LexicalUnits.AnyAsync(ct))
        {
            return;
        }

        var languages = await db.Languages.ToDictionaryAsync(l => l.Code, ct);
        if (!languages.TryGetValue("en", out var english) || !languages.TryGetValue("ru", out var russian))
        {
            return;
        }

        var words = new (string Text, string? Transcription, string Translation, PartOfSpeech Pos, CefrLevel Level, string Example, string Native, int Rank)[]
        {
            ("hello", null, "привет", PartOfSpeech.Interjection, CefrLevel.A1, "Hello, how are you?", "Привет, как дела?", 1),
            ("water", "/ˈwɔːtər/", "вода", PartOfSpeech.Noun, CefrLevel.A1, "I drink water every morning.", "Я пью воду каждое утро.", 2),
            ("book", null, "книга", PartOfSpeech.Noun, CefrLevel.A1, "This book is very interesting.", "Эта книга очень интересная.", 3),
            ("friend", null, "друг", PartOfSpeech.Noun, CefrLevel.A1, "She is my best friend.", "Она моя лучшая подруга.", 4),
            ("house", null, "дом", PartOfSpeech.Noun, CefrLevel.A1, "They live in a small house.", "Они живут в маленьком доме.", 5),
            ("work", "/wɜːrk/", "работать", PartOfSpeech.Verb, CefrLevel.A1, "I work from home on Fridays.", "Я работаю из дома по пятницам.", 6),
            ("eat", "/iːt/", "есть, кушать", PartOfSpeech.Verb, CefrLevel.A1, "We eat dinner at seven.", "Мы ужинаем в семь.", 7),
            ("go", null, "идти, ехать", PartOfSpeech.Verb, CefrLevel.A1, "Let's go to the park.", "Пойдём в парк.", 8),
            ("big", null, "большой", PartOfSpeech.Adjective, CefrLevel.A1, "The city is very big.", "Город очень большой.", 9),
            ("happy", null, "счастливый", PartOfSpeech.Adjective, CefrLevel.A1, "I am happy to see you.", "Я рад тебя видеть.", 10),
            ("quickly", null, "быстро", PartOfSpeech.Adverb, CefrLevel.A2, "She finished the task quickly.", "Она быстро закончила задачу.", 11),
            ("always", null, "всегда", PartOfSpeech.Adverb, CefrLevel.A1, "He always drinks coffee in the morning.", "Он всегда пьёт кофе утром.", 12),
            ("because", null, "потому что", PartOfSpeech.Conjunction, CefrLevel.A2, "I stayed home because it rained.", "Я остался дома, потому что шёл дождь.", 13),
            ("beautiful", null, "красивый", PartOfSpeech.Adjective, CefrLevel.A2, "What a beautiful day!", "Какой прекрасный день!", 14),
            ("learn", null, "учить, изучать", PartOfSpeech.Verb, CefrLevel.A1, "I want to learn Italian.", "Я хочу выучить итальянский.", 15),
            ("travel", null, "путешествовать", PartOfSpeech.Verb, CefrLevel.A2, "We travel every summer.", "Мы путешествуем каждое лето.", 16),
            ("appointment", null, "приём, встреча", PartOfSpeech.Noun, CefrLevel.B1, "I have an appointment at three.", "У меня приём в три часа.", 17),
            ("experience", null, "опыт", PartOfSpeech.Noun, CefrLevel.B1, "That was a great experience.", "Это был отличный опыт.", 18),
            ("improve", null, "улучшать", PartOfSpeech.Verb, CefrLevel.B2, "My listening has improved a lot.", "Мой аудиослушание сильно улучшилось.", 19),
            ("therefore", null, "поэтому", PartOfSpeech.Adverb, CefrLevel.B2, "It rained; therefore we stayed inside.", "Шёл дождь; поэтому мы остались внутри.", 20)
        };

        foreach (var w in words)
        {
            var unit = new LexicalUnit
            {
                LanguageId = english.Id,
                TranslationLanguageId = russian.Id,
                Text = w.Text,
                NormalizedText = TextNormalizer.Normalize(w.Text),
                Transcription = w.Transcription,
                Translation = w.Translation,
                PartOfSpeech = w.Pos,
                ExampleTarget = w.Example,
                ExampleNative = w.Native,
                MinLearnerLevel = w.Level,
                FrequencyRank = w.Rank,
                Status = ContentStatus.Verified,
                Tags = ["seed"]
            };
            unit.ContentHash = TextNormalizer.ContentHash(unit.Text, unit.Translation, unit.ExampleTarget);
            db.LexicalUnits.Add(unit);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} dictionary words", await db.LexicalUnits.CountAsync(ct));
    }

    private async Task SeedDemoExercisesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Exercises.AnyAsync(ct))
        {
            return;
        }

        var languages = await db.Languages.ToDictionaryAsync(l => l.Code, ct);
        if (!languages.TryGetValue("en", out var english))
        {
            return;
        }

        db.Exercises.Add(new Exercise
        {
            LanguageId = english.Id,
            Type = ExerciseType.MultipleChoice,
            Title = "Everyday actions",
            Instructions = "Выберите верный вариант.",
            Level = CefrLevel.A1,
            Points = 30,
            EstimatedSeconds = 90,
            Source = ContentSource.Curated,
            IsPublished = true,
            Payload = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "multiple_choice",
                ["items"] = new System.Text.Json.Nodes.JsonArray(
                    Item("q1", "I ___ water every morning.", "drink", 0, "drink — present simple, 1st person"),
                    Item("q2", "She ___ to work by train.", "travels", 0, "travels — 3rd person singular takes -s"),
                    Item("q3", "They ___ dinner at seven.", "eat", 0, "eat — plural subject, base form"),
                    Item("q4", "It ___ outside.", "rains", 0, "rains — impersonal verb, 3rd person"))
            }
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} demo exercises", await db.Exercises.CountAsync(ct));
    }

    private async Task SeedAdminAsync(string email, string password, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return;
        }

        var languages = await db.Languages.OrderBy(l => l.SortOrder).ToListAsync(ct);
        var hasher = new BcryptPasswordHasher();
        var user = new User
        {
            Email = email,
            PasswordHash = hasher.Hash(password),
            DisplayName = "Администратор",
            Role = UserRole.Admin,
            InterfaceLanguageId = languages.First(l => l.Code == "ru").Id,
            TargetLanguageId = languages.First(l => l.Code == "en").Id,
            EmailConfirmed = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Seeded admin account {Email}", email);
    }

    private static System.Text.Json.Nodes.JsonObject Item(string id, string question, string correct, int correctIndex, string explanation) =>
        new()
        {
            ["id"] = id,
            ["question"] = question,
            ["question_language"] = "target",
            ["options"] = new System.Text.Json.Nodes.JsonArray(correct, Distractor(correct), Distractor(correct + "!"), Distractor(correct.ToLowerInvariant())),
            ["correct_index"] = correctIndex,
            ["explanation"] = explanation
        };

    private static string Distractor(string value) => value switch
    {
        "drink" => "drinks",
        "drinks" => "drinking",
        "travels" => "travel",
        "eat" => "eats",
        "rains" => "rain",
        _ => value + "ed"
    };

    private static string AccentFor(string code) => code switch
    {
        "en" => "#4F6BED",
        "de" => "#D64545",
        "es" => "#E8A33D",
        "fr" => "#2E86AB",
        "it" => "#3A9D5D",
        "ja" => "#C2456B",
        "zh" => "#B33A3A",
        _ => "#6B7280"
    };

    private static string LessonTitle(int index) => index switch
    {
        1 => "Приветствия и знакомство",
        2 => "Семья и общение",
        3 => "Еда и напитки",
        4 => "Путешествие и транспорт",
        5 => "Работа и учёба",
        _ => "Повторение"
    };

    private static string[] StarterVocabulary(int lesson) => lesson switch
    {
        1 => ["hello", "hi", "nice to meet you"],
        2 => ["friend", "family", "brother"],
        3 => ["water", "eat", "delicious"],
        4 => ["go", "train", "ticket"],
        5 => ["work", "learn", "office"],
        _ => []
    };

    private static string BuildLessonMarkdown(string language, string level, string title) =>
        $"""
         # {title}

         **Язык:** {language} · **Уровень:** CEFR {level}

         ## Цели
         - познакомиться с новой лексикой по теме
         - отработать базовые конструкции
         - выполнить 10–15 упражнений

         ## Материал
         1. Прочитайте примеры и выучите слова из списка.
         2. Прослушайте аудио и повторите вслух.
         3. Сгенерируйте упражнения через `POST /api/v1/exercises/generate`.

         ## Практика
         Используйте чат-репетитора (`POST /api/v1/tutor/sessions`) в режиме `LessonRoleplay`.
         """;
}