# INIT – Contexto do Projeto VideoCuts

Arquivo de recuperação de contexto para ferramentas e agentes. Use para retomar o entendimento do projeto sem ler todo o código.

---

## Visão geral

- **Objetivo:** pipeline de vídeo para short-form: opcionalmente baixar vídeo (URL direta ou YouTube), transcrever, identificar momentos engajantes (LLM) e gerar clipes (corte + opcional 9:16).
- **Stack:** .NET 8, Clean Architecture. FFmpeg (FFMpegCore), Whisper (API OpenAI), LLM (OpenAI ou Ollama local), download YouTube (YoutubeExplode).
- **Solução:** compilar com `dotnet build` na raiz; testes em `tests/VideoCuts.Tests`.

---

## Projetos e referências

| Projeto | Caminho | Referencia |
|---------|---------|------------|
| VideoCuts.Core | `src/VideoCuts.Core/` | Nenhum |
| VideoCuts.Infrastructure | `src/VideoCuts.Infrastructure/` | Core |
| VideoCuts.Worker | `src/VideoCuts.Worker/` | Core, Infrastructure |
| VideoCuts.Cli | `src/VideoCuts.Cli/` | Core, Infrastructure |
| VideoCuts.Tests | `tests/VideoCuts.Tests/` | Core, Infrastructure |

---

## Core – Interfaces (contratos)

Todas em `src/VideoCuts.Core/Interfaces/`:

- **IVideoDownloader** – `DownloadAsync(VideoSource, DownloadOptions)` → `DownloadResult` (Success, LocalPath, ErrorMessage).
- **ITranscriptionService** – `TranscribeAsync(videoPath, TranscriptionOptions)` → `TranscriptionResult` (Segments com Start/End/Text, FullText).
- **ICutDetectionService** – `DetectCutsAsync(videoPath, CutDetectionOptions)` → `CutDetectionResult`. (Não usado no pipeline atual.)
- **IVideoEditor** – `EditAsync(EditRequest)` → `EditResult`. EditRequest: InputVideoPath, SegmentsToKeep (CutSegment), OutputPath, ConvertToVertical.
- **IEngagingMomentsService** – `GetEngagingMomentsAsync(transcriptText)` ou `GetEngagingMomentsAsync(segments)` → `IReadOnlyList<VideoCut>` (StartSeconds, EndSeconds, Description).
- **IVideoClipPipeline** – `RunAsync(PipelineRequest)` → `PipelineResult` (Success, LocalVideoPath, Transcript, Cuts, GeneratedClips, ErrorMessage, StageTimings).

---

## Core – Modelos principais

- **VideoDownload:** `VideoSource` (Url, Title), `DownloadOptions` (OutputDirectory, Format, Quality), `DownloadResult`.
- **Transcription:** `TranscriptSegment` (StartTimeSeconds, EndTimeSeconds, Text), `TranscriptionOptions`, `TranscriptionResult`.
- **CutDetection:** `CutSegment`, `CutDetectionOptions`, `CutDetectionResult`.
- **CutSuggestion:** `VideoCut` (StartSeconds, EndSeconds, Description).
- **VideoEditing:** `EditRequest`, `EditResult`.
- **Pipeline:** `PipelineRequest` (Source, LocalVideoPath, DownloadIfUrl, OutputDirectory, ConvertClipsToVertical, MaxClips, TranscriptionOptions), `PipelineResult`, `GeneratedClip` (Cut, OutputPath, Index).
- **Configuration:** `VideoCutsSettings` (SectionName "VideoCuts", MaxClips, ConvertToVertical, OutputDirectory).

---

## Infrastructure – Implementações

- **VideoEditing:** `FfmpegVideoEditor` – IVideoEditor com FFMpegCore (corte por segmento, opção 9:16).
- **Transcription:** `OpenAiWhisperTranscriptionService` (API Whisper), `WhisperTranscriptionService` (Whisper.net local).
- **EngagingMoments:**
  - `LlmEngagingMomentsService` – chat completions OpenAI-compatível, resposta JSON `{ "cuts": [ { "start", "end", "description" } ] }`.
  - `OllamaEngagingMomentsService` – POST http://localhost:11434/api/generate (modelo configurável, ex.: llama3), mesmo formato JSON de cortes.
  - `FallbackEngagingMomentsService` – decorator: tenta primary (ex.: Ollama), em exceção usa fallback (ex.: OpenAI); log de aviso no fallback.
  - `EngagingMomentsJsonParser` – parser estático compartilhado: string JSON → `IReadOnlyList<VideoCut>` (strip ```json, filtra start &lt; 0 ou end ≤ start).
- **VideoDownload:**
  - `HttpVideoDownloader` – URL HTTP/HTTPS direta (arquivo .mp4 etc.).
  - `YoutubeExplodeVideoDownloader` – URLs YouTube (youtube.com, youtu.be) via YoutubeExplode; salva em OutputDirectory com nome do vídeo.
- **Pipeline:** `VideoClipPipeline` – ITranscriptionService, IEngagingMomentsService, IVideoEditor, ILogger, IVideoDownloader (opcional), IOptions&lt;VideoCutsSettings&gt; (opcional). Resolve path (download se Source + DownloadIfUrl), transcreve, obtém cortes (IEngagingMomentsService), gera clipes via IVideoEditor; MaxClips e OutputDirectory podem vir do request ou das settings.

---

## CLI – Pontos de entrada

- **Pipeline:** `pipeline --input &lt;arquivo&gt; | --url &lt;url&gt;` com opções `--output-dir`, `--max-clips`, `--no-vertical`, `--ollama`, `--ollama-fallback`. URL YouTube → YoutubeExplode; outras URLs → HttpVideoDownloader.
- **Batch:** `batch &lt;pasta&gt;` – processa todos os vídeos da pasta (extensões comuns); opções iguais ao pipeline + `--json` para saída estruturada; falha por arquivo não interrompe o lote.
- **Corte manual:** `&lt;video&gt; &lt;início_seg&gt; &lt;fim_seg&gt; [saída] [--no-vertical]` – um trecho, sem transcrição/LLM.

---

## Testes (VideoCuts.Tests)

- **xUnit + Moq.** Foco em lógica de negócio; sem I/O pesado (exceto arquivo temporário no pipeline).
- **EngagingMomentsJsonParserTests:** parsing de JSON de LLM → VideoCut (válido, vazio, inválido, wrapper markdown, intervalos inválidos filtrados).
- **VideoClipPipelineTests:** pipeline com mocks de ITranscriptionService, IEngagingMomentsService, IVideoEditor; sucesso com clipes, falha de transcrição, zero cortes, MaxClips, path local inválido.

Execução: `dotnet test tests/VideoCuts.Tests`.

---

## Configuração e DI

- **VideoCutsSettings** – seção `VideoCuts` no appsettings; injetado como `IOptions<VideoCutsSettings>`.
- **OllamaEngagingMomentsOptions** – seção `Ollama` (BaseUrl, Model, RetryCount, RetryDelayMs); usado por `OllamaEngagingMomentsService`.
- **Extensões (Infrastructure):** `AddOllamaEngagingMoments(services, configuration)`, `AddEngagingMomentsWithOllamaFallback(services, configuration)` (keyed services: primary Ollama, fallback OpenAI).

---

## Variáveis de ambiente / requisitos

- **OPENAI_API_KEY** – obrigatória para transcrição Whisper e para LLM quando não se usa apenas Ollama (ou como fallback).
- **FFmpeg** – no PATH ou `GlobalFFOptions`.
- **Ollama (opcional)** – servidor em http://localhost:11434 com modelo (ex.: llama3) para `OllamaEngagingMomentsService` ou fallback.

---

## Convenções

- Core: só interfaces e modelos (records); sem referências a Infrastructure ou pacotes de terceiros.
- Nomes de pastas em PascalCase; namespaces alinhados às pastas.
- CancellationToken em métodos async quando fizer sentido.
- Respostas com `Success` e `ErrorMessage` quando aplicável.
- Downloader no pipeline é opcional; quando há URL, a CLI escolhe YoutubeExplode (YouTube) ou Http (demais URLs).
