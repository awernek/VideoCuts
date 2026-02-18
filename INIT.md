# INIT – Contexto do Projeto VideoCuts

Arquivo de recuperação de contexto para ferramentas e agentes. Use para retomar o entendimento do projeto sem ler todo o código.

---

## Visão geral

- **Objetivo:** pipeline de vídeo para short-form: opcionalmente baixar vídeo, transcrever, identificar momentos engajantes (LLM) e gerar clipes (corte + opcional 9:16).
- **Stack:** .NET 8, Clean Architecture. FFmpeg (FFMpegCore), Whisper (Whisper.net ou API OpenAI), LLM (API OpenAI compatível).
- **Solução:** `VideoCuts.slnx` (formato .slnx). Compilar com `dotnet build VideoCuts.slnx`.

---

## Projetos e referências

| Projeto | Caminho | Referencia |
|---------|---------|------------|
| VideoCuts.Core | `src/VideoCuts.Core/` | Nenhum |
| VideoCuts.Infrastructure | `src/VideoCuts.Infrastructure/` | Core |
| VideoCuts.Worker | `src/VideoCuts.Worker/` | Core, Infrastructure |
| VideoCuts.Cli | `src/VideoCuts.Cli/` | Core, Infrastructure |

---

## Core – Interfaces (contratos)

Todas em `src/VideoCuts.Core/Interfaces/`:

- **IVideoDownloader** – `DownloadAsync(VideoSource, DownloadOptions)` → `DownloadResult` (LocalPath).
- **ITranscriptionService** – `TranscribeAsync(videoPath, TranscriptionOptions)` → `TranscriptionResult` (Segments com Start/End/Text, FullText).
- **ICutDetectionService** – `DetectCutsAsync(videoPath, CutDetectionOptions)` → `CutDetectionResult` (Segments). (Não usado no pipeline atual.)
- **IVideoEditor** – `EditAsync(EditRequest)` → `EditResult`. EditRequest: InputVideoPath, SegmentsToKeep (lista de CutSegment), OutputPath, ConvertToVertical.
- **IEngagingMomentsService** – `GetEngagingMomentsAsync(transcriptText ou segments)` → `IReadOnlyList<VideoCut>` (StartSeconds, EndSeconds, Description).
- **IVideoClipPipeline** – `RunAsync(PipelineRequest)` → `PipelineResult` (LocalVideoPath, Transcript, Cuts, GeneratedClips).

---

## Core – Modelos principais

- **VideoDownload:** `VideoSource` (Url, Title), `DownloadOptions`, `DownloadResult`.
- **Transcription:** `TranscriptSegment` (StartTimeSeconds, EndTimeSeconds, Text; Start/End são aliases), `TranscriptionOptions`, `TranscriptionResult`.
- **CutDetection:** `CutSegment` (StartTimeSeconds, EndTimeSeconds, SegmentType), `CutDetectionOptions`, `CutDetectionResult`.
- **CutSuggestion:** `VideoCut` (StartSeconds, EndSeconds, Description).
- **VideoEditing:** `EditRequest` (InputVideoPath, SegmentsToKeep, OutputPath, OutputFormat, ConvertToVertical), `EditResult`.
- **Pipeline:** `PipelineRequest` (Source, LocalVideoPath, DownloadIfUrl, OutputDirectory, ConvertClipsToVertical, MaxClips, TranscriptionOptions), `PipelineResult` (Success, LocalVideoPath, Transcript, Cuts, GeneratedClips, ErrorMessage), `GeneratedClip` (Cut, OutputPath, Index).

---

## Infrastructure – Implementações

- **VideoEditing:** `FfmpegVideoEditor` – implementa IVideoEditor com FFMpegCore (corte por segmento, opção 9:16).
- **Transcription:** `WhisperTranscriptionService` (Whisper.net, modelo local; extrai áudio com FFMpeg para 16 kHz WAV), `OpenAiWhisperTranscriptionService` (API Whisper, verbose_json → segmentos).
- **EngagingMoments:** `LlmEngagingMomentsService` – chat completions (OpenAI-compatível), prompt “Identify the most engaging moments for short-form content.”, resposta JSON `{ "cuts": [ { "start", "end", "description" } ] }` → lista de VideoCut.
- **Pipeline:** `VideoClipPipeline` – recebe ITranscriptionService, IEngagingMomentsService, IVideoEditor, IVideoDownloader (opcional). Resolve path (download se Source + DownloadIfUrl), transcreve, obtém cortes, gera um clipe por VideoCut via IVideoEditor.

Nenhuma implementação de **IVideoDownloader** nem **ICutDetectionService** ainda; o pipeline usa apenas path local e IEngagingMomentsService para “cortes”.

---

## Convenções

- Core: só interfaces e modelos (records); sem referências a Infrastructure ou a pacotes de terceiros.
- Nomes de pastas em PascalCase; namespaces alinhados às pastas (ex.: `VideoCuts.Infrastructure.Pipeline`).
- CancellationToken em métodos async quando fizer sentido.
- Respostas de serviço com `Success` e `ErrorMessage` quando aplicável.

---

## Pontos de entrada

- **CLI:** `src/VideoCuts.Cli/Program.cs` – hoje focado em exemplo do editor (args: video, start, end, [output], [--no-vertical]).
- **Worker:** `src/VideoCuts.Worker/` – worker service; integrar com IVideoClipPipeline conforme necessidade.

---

## Variáveis de ambiente / configuração

- **OpenAI (transcrição + LLM):** chave em variável de ambiente (ex.: `OPENAI_API_KEY`) ou configuração da aplicação.
- **FFmpeg:** no PATH ou `GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "..." })`.
- **Whisper local:** caminho do modelo (ex.: ggml-base.bin) passado ao construtor de `WhisperTranscriptionService`.
