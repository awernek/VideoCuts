# VideoCuts

Pipeline de processamento de vídeo para geração de clipes short-form: download (opcional), transcrição, detecção de momentos engajantes via LLM e edição com FFmpeg.

## Como usar (CLI)

Não há interface gráfica; o uso é pela linha de comando.

### 1. Pipeline completo (URL ou arquivo → clipes automáticos)

Transcreve o vídeo, envia a transcrição para um LLM para identificar os melhores momentos e gera os clipes.

**Requisito:** defina a variável de ambiente `OPENAI_API_KEY` (usada na transcrição Whisper). Para os cortes, pode usar OpenAI, Ollama (local) ou fallback Ollama → OpenAI.

**Com arquivo no disco:**
```bash
set OPENAI_API_KEY=sua-chave-aqui
dotnet run --project src/VideoCuts.Cli -- pipeline --input C:\videos\entrada.mp4 --output-dir C:\clips
```

**Com URL do YouTube (YoutubeExplode):**
```bash
set OPENAI_API_KEY=sua-chave-aqui
dotnet run --project src/VideoCuts.Cli -- pipeline --url "https://www.youtube.com/watch?v=VIDEO_ID" --output-dir ./clips --max-clips 5
```

**Com URL direta (HTTP/HTTPS):**
```bash
set OPENAI_API_KEY=sua-chave-aqui
dotnet run --project src/VideoCuts.Cli -- pipeline --url "https://exemplo.com/video.mp4" --output-dir ./clips
```

**Opções do pipeline:**
- `--input`, `-i`           Caminho do vídeo no disco.
- `--url`, `-u`             URL do vídeo. **YouTube** (youtube.com / youtu.be) usa YoutubeExplode; outras URLs usam download HTTP direto.
- `--output-dir`, `-o`      Pasta onde salvar os clipes (padrão: mesma pasta do vídeo).
- `--max-clips`             Número máximo de clipes a gerar (opcional).
- `--no-vertical`           Não converter os clipes para formato vertical 9:16.
- `--ollama`                Usar apenas **Ollama** (localhost) para identificar cortes (modelo configurável em appsettings, ex.: llama3).
- `--ollama-fallback`       Tentar Ollama primeiro; em falha, usar OpenAI para cortes.

### 2. Batch (pasta com vários vídeos)

Processa todos os vídeos de uma pasta (transcrição → cortes → clipes). Falhas por arquivo não interrompem o lote.

```bash
set OPENAI_API_KEY=sua-chave-aqui
dotnet run --project src/VideoCuts.Cli -- batch C:\videos --output-dir C:\clips --max-clips 3
dotnet run --project src/VideoCuts.Cli -- batch ./videos --ollama-fallback --json
```

**Opções do batch:** as mesmas do pipeline (`--output-dir`, `--max-clips`, `--no-vertical`, `--ollama`, `--ollama-fallback`) e ainda:
- `--json`   Saída final em JSON (FilePath, Success, Clips, Error) por arquivo.

Formatos de vídeo considerados: .mp4, .mkv, .avi, .mov, .webm, .m4v, .wmv, .flv.

### 3. Corte manual (um trecho)

Corta um único intervalo do vídeo (sem transcrição nem LLM). Não precisa de `OPENAI_API_KEY`.

```bash
dotnet run --project src/VideoCuts.Cli -- video.mp4 10 60
# Corta do segundo 10 ao 60 e converte para 9:16

dotnet run --project src/VideoCuts.Cli -- video.mp4 0 30 saida.mp4 --no-vertical
# Corta do 0 ao 30s, mantém proporção, salva em saida.mp4
```

## Arquitetura

Solução .NET 8 em **Clean Architecture**:

| Projeto | Tipo | Responsabilidade |
|---------|------|------------------|
| **VideoCuts.Core** | Class library | Interfaces e modelos de domínio (sem implementação) |
| **VideoCuts.Infrastructure** | Class library | Implementações: FFmpeg, Whisper, OpenAI, Ollama, YoutubeExplode, pipeline |
| **VideoCuts.Worker** | Worker service | Execução em background (a conectar ao pipeline) |
| **VideoCuts.Cli** | Console app | Uso via linha de comando |
| **VideoCuts.Tests** | Test project | Testes unitários (xUnit, Moq): parser LLM → VideoCut, pipeline com mocks |

**Dependências:** Infrastructure → Core; Worker e Cli → Core + Infrastructure; Tests → Core + Infrastructure.

## Estrutura do Core

- **Interfaces:** `IVideoDownloader`, `ITranscriptionService`, `ICutDetectionService`, `IVideoEditor`, `IEngagingMomentsService`, `IVideoClipPipeline`
- **Modelos:** por domínio em `Models/` (VideoDownload, Transcription, CutDetection, CutSuggestion, VideoEditing, Pipeline)
- **Configuração:** `VideoCutsSettings` (MaxClips, ConvertToVertical, OutputDirectory)

## Pré-requisitos

- .NET 8 SDK
- **FFmpeg** no PATH (ou configurar `GlobalFFOptions` do FFMpegCore)
- **Transcrição:** API OpenAI (Whisper) – variável `OPENAI_API_KEY`
- **Cortes (LLM):** OpenAI (padrão), ou **Ollama** em http://localhost:11434 (modelo ex.: llama3), ou fallback Ollama → OpenAI

## Build e execução

```bash
# Restaurar e compilar (a partir da raiz do repositório)
dotnet restore
dotnet build

# CLI
dotnet run --project src/VideoCuts.Cli -- pipeline --input video.mp4 --output-dir ./clips
dotnet run --project src/VideoCuts.Cli -- batch ./videos
dotnet run --project src/VideoCuts.Cli -- video.mp4 10 60

# Testes
dotnet test tests/VideoCuts.Tests
```

## Fluxo do pipeline

1. **Download (opcional)** – Se `--url`: YouTube usa `YoutubeExplodeVideoDownloader`, outras URLs usam `HttpVideoDownloader`. Sem URL, usa `--input` (arquivo local).
2. **Transcrição** – `ITranscriptionService` (OpenAI Whisper API) gera texto com timestamps.
3. **Cortes** – `IEngagingMomentsService`: OpenAI, Ollama ou fallback (Ollama → OpenAI). Resposta JSON `{ "cuts": [ { "start", "end", "description" } ] }` parseada por `EngagingMomentsJsonParser`.
4. **Clipes** – `IVideoEditor` (FFmpeg) corta cada intervalo e opcionalmente converte para 9:16.

O orquestrador é `IVideoClipPipeline` / `VideoClipPipeline`: recebe `PipelineRequest` e retorna `PipelineResult` (transcrição, cortes, paths dos clipes).

## Configuração

- **appsettings.json (CLI/Worker):**
  - `VideoCuts`: MaxClips, ConvertToVertical, OutputDirectory
  - `Ollama`: BaseUrl (ex.: http://localhost:11434), Model (ex.: llama3), RetryCount, RetryDelayMs
- **Variável de ambiente:** `OPENAI_API_KEY` para transcrição (e para fallback de cortes quando usar `--ollama-fallback`).

## Implementações principais (Infrastructure)

- **Download:** `HttpVideoDownloader` (URL direta), `YoutubeExplodeVideoDownloader` (YouTube via YoutubeExplode)
- **Transcrição:** `OpenAiWhisperTranscriptionService`
- **Cortes:** `LlmEngagingMomentsService` (OpenAI), `OllamaEngagingMomentsService` (Ollama), `FallbackEngagingMomentsService` (Ollama → OpenAI)
- **Edição:** `FfmpegVideoEditor`
- **Pipeline:** `VideoClipPipeline(transcription, engagingMoments, editor, logger, downloader?, options?)`

## Licença

Uso interno / sob definição do repositório.
