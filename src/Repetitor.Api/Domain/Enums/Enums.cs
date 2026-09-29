namespace Repetitor.Api.Domain.Enums;

public enum UserRole
{
    Learner = 0,
    Teacher = 1,
    Admin = 2
}

public enum CefrLevel
{
    A1 = 1,
    A2 = 2,
    B1 = 3,
    B2 = 4,
    C1 = 5,
    C2 = 6
}

public enum CardState
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3,
    Mastered = 4
}

public enum ReviewRating
{
    Again = 0,
    Hard = 1,
    Good = 2,
    Easy = 3
}

public enum ContentStatus
{
    Draft = 0,
    Verified = 1,
    Deprecated = 2
}

public enum PartOfSpeech
{
    Unknown = 0,
    Noun = 1,
    Verb = 2,
    Adjective = 3,
    Adverb = 4,
    Pronoun = 5,
    Preposition = 6,
    Conjunction = 7,
    Interjection = 8,
    Numeral = 9,
    Article = 10,
    Phrase = 11,
    Expression = 12
}

public enum ExerciseType
{
    MultipleChoice = 1,
    TranslateToTarget = 2,
    TranslateFromTarget = 3,
    GapFill = 4,
    WordOrder = 5,
    MatchPairs = 6,
    Listening = 7,
    Writing = 8,
    Speaking = 9,
    FillInTheBlanks = 10
}

public enum ContentSource
{
    Curated = 0,
    AiGenerated = 1,
    UserCreated = 2,
    Imported = 3
}

public enum ChatRole
{
    System = 0,
    User = 1,
    Assistant = 2
}

public enum TutorMode
{
    FreePractice = 0,
    LessonRoleplay = 1,
    ExamPreparation = 2,
    GrammarHelp = 3,
    Interview = 4,
    Travel = 5
}

public enum MediaKind
{
    GeneratedSpeech = 0,
    UserRecording = 1,
    LessonAudio = 2,
    PronunciationSample = 3
}

public enum AiOperation
{
    ChatCompletion = 0,
    Embedding = 1,
    TextToSpeech = 2,
    SpeechToText = 3,
    ExerciseGeneration = 4,
    AnswerGrading = 5,
    PronunciationAssessment = 6
}

public enum LessonProgressStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

/// <summary>
/// Состояние фоновой генерации содержимого урока.
/// </summary>
public enum LessonGenerationStatus
{
    /// <summary>Генерация не запрашивалась либо результат уже вручную изменили.</summary>
    None = 0,
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}
