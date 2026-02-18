# VideoCuts

Pipeline de processamento de vídeo para geração de clipes short-form: download (opcional), transcrição, detecção de momentos engajantes via LLM e edição com FFmpeg.

## Arquitetura

Solução .NET 8 em **Clean Architecture**:

| Projeto | Tipo | Responsabilidade |
|---------|------|------------------|
| **VideoCuts.Core** | Class library | Interfaces e modelos de domínio (sem implementação) |
| **VideoCuts.Infrastructure** | Class library | Implementações: FFmpeg, Whisper, OpenAI/LLM, pipeline |
| **VideoCuts.Worker** | Worker service | Execução em background (a conectar ao pipeline) |
| **VideoCuts.Cli** | Console app | Uso via linha de comando (editor e exemplos) |

**Dependências entre projetos:** Infrastructure → Core; Worker e Cli → Core + Infrastructure.

## Estrutura do Core

- **Interfaces:** `IVideoDownloader`, `ITranscriptionService`, `ICutDetectionService`, `IVideoEditor`, `IEngagingMomentsService`, `IVideoClipPipeline`
- **Modelos:** por domínio em `Models/` (VideoDownload, Transcription, CutDetection, CutSuggestion, VideoEditing, Pipeline)

## Pré-requisitos

- .NET 8 SDK
- **FFmpeg** no PATH (ou configurar `GlobalFFOptions` do FFMpegCore)
- Para transcrição local: modelo Whisper (ex.: ggml-base.bin) ou API OpenAI
- Para cortes via LLM: API OpenAI (ou endpoint compatível)

## Build e execução

```bash
# Restaurar e compilar
dotnet restore
dotnet build VideoCuts.slnx

# CLI – cortar vídeo e converter para vertical 9:16
dotnet run --project src/VideoCuts.Cli -- --help
dotnet run --project src/VideoCuts.Cli -- video.mp4 10 60
dotnet run --project src/VideoCuts.Cli -- video.mp4 0 30 saida.mp4 --no-vertical
```

## Fluxo do pipeline

1. **Download (opcional)** – `IVideoDownloader` baixa a partir de URL.
2. **Transcrição** – `ITranscriptionService` (Whisper.net local ou OpenAI Whisper API) gera texto com timestamps.
3. **Cortes** – `IEngagingMomentsService` envia transcrição ao LLM e recebe lista de `VideoCut` (start, end, description).
4. **Clipes** – `IVideoEditor` (FFmpeg) corta cada intervalo e opcionalmente converte para 9:16.

O orquestrador é `IVideoClipPipeline` / `VideoClipPipeline`: recebe `PipelineRequest` (path ou URL, opções) e retorna `PipelineResult` (transcrição, cortes, paths dos clipes).

## Configuração típica

- **Transcrição:** `WhisperTranscriptionService` (modelo local) ou `OpenAiWhisperTranscriptionService` (API).
- **Cortes:** `LlmEngagingMomentsService` (OpenAI chat com prompt para “engaging moments”).
- **Edição:** `FfmpegVideoEditor` (corte + opção vertical 9:16).
- **Pipeline:** `VideoClipPipeline(transcription, engagingMoments, editor, downloader?)`.

## Licença

Uso interno / sob definição do repositório.
