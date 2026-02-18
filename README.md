# VideoCuts

Pipeline de processamento de vídeo para geração de clipes short-form: download (opcional), transcrição, detecção de momentos engajantes via LLM e edição com FFmpeg.

## Como usar (CLI)

Não há interface gráfica; o uso é pela linha de comando.

### 1. Pipeline completo (URL ou arquivo → clipes automáticos)

Transcreve o vídeo, envia a transcrição para um LLM para identificar os melhores momentos e gera os clipes.

**100% local (sem API key):** use `--whisper-local` + `--whisper-model <caminho>` para transcrição local (Whisper.net) e `--ollama` para cortes. Não é necessário definir `OPENAI_API_KEY`. Você precisa baixar um modelo Whisper em formato GGML (ex.: ggml-base.bin) e informar o caminho.

**Transcrição:** pode ser (1) **local** — `--whisper-local` com `--whisper-model <arquivo.bin>` ou `Whisper:ModelPath` no appsettings; ou (2) **API OpenAI** — sem `--whisper-local`, exige `OPENAI_API_KEY`.

**Cortes:** use `--ollama` para Ollama (localhost, ex.: llama3). Com transcrição local + Ollama, o fluxo fica todo na sua máquina.

**Pré-requisitos:** FFmpeg no PATH; para 100% local: Ollama rodando + modelo Whisper (ex.: [ggml-base](https://huggingface.co/ggerganov/whisper.cpp) em formato .bin).

**Exemplo 100% local (YouTube → clipes, sem OPENAI_API_KEY):**
```bash
dotnet run --project src/VideoCuts.Cli -- pipeline --url "https://www.youtube.com/watch?v=VIDEO_ID" --output-dir ./clips --whisper-local --whisper-model C:\Models\ggml-base.bin --ollama
```

**Com arquivo no disco (local):**
```bash
dotnet run --project src/VideoCuts.Cli -- pipeline --input C:\videos\entrada.mp4 --output-dir C:\clips --whisper-local --whisper-model C:\Models\ggml-base.bin --ollama
```

**Usando transcrição na nuvem (OpenAI Whisper) e cortes com Ollama:**
```bash
# Defina OPENAI_API_KEY no ambiente
dotnet run --project src/VideoCuts.Cli -- pipeline --input video.mp4 --output-dir ./clips --ollama
```

**Opções do pipeline:**
- `--input`, `-i`           Caminho do vídeo no disco.
- `--url`, `-u`             URL do vídeo. **YouTube** (youtube.com / youtu.be) usa YoutubeExplode; outras URLs usam download HTTP direto.
- `--output-dir`, `-o`      Pasta onde salvar os clipes (padrão: mesma pasta do vídeo).
- `--max-clips`             Número máximo de clipes a gerar (opcional).
- `--no-vertical`           Não converter os clipes para formato vertical 9:16.
- `--whisper-local`         Transcrição local (Whisper.net). Requer `--whisper-model` ou `Whisper:ModelPath` no appsettings.
- `--whisper-model`         Caminho do modelo Whisper (ex.: ggml-base.bin). Use com `--whisper-local`.
- `--ollama`                Usar **Ollama** (localhost) para identificar cortes — gratuito, modelo ex.: llama3.
- `--ollama-fallback`       Tentar Ollama primeiro; em falha, usar OpenAI para cortes (exige `OPENAI_API_KEY`).

### 2. Batch (pasta com vários vídeos)

Processa todos os vídeos de uma pasta (transcrição → cortes → clipes). Falhas por arquivo não interrompem o lote. Use `--whisper-local` + `--ollama` para fluxo 100% local.

```bash
dotnet run --project src/VideoCuts.Cli -- batch C:\videos --output-dir C:\clips --max-clips 3 --whisper-local --whisper-model C:\Models\ggml-base.bin --ollama
dotnet run --project src/VideoCuts.Cli -- batch ./videos --whisper-local --whisper-model ./ggml-base.bin --ollama --json
```

**Opções do batch:** as mesmas do pipeline (`--output-dir`, `--max-clips`, `--no-vertical`, `--whisper-local`, `--whisper-model`, `--ollama`, `--ollama-fallback`) e ainda:
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
- **Cortes (LLM):** **Ollama** em http://localhost:11434 (recomendado: gratuito, local; modelo ex.: llama3). Opcional: OpenAI ou fallback Ollama → OpenAI.
- **Transcrição:** local com `--whisper-local` + `--whisper-model` (modelo GGML, ex.: ggml-base.bin) ou API OpenAI (variável `OPENAI_API_KEY`).

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
- **Whisper (transcrição local):** `Whisper:ModelPath` — caminho do modelo (ex.: ggml-base.bin). Usado com `--whisper-local` quando não se passa `--whisper-model`.
- **Variável de ambiente:** `OPENAI_API_KEY` só é exigida se **não** usar `--whisper-local` ou se usar `--ollama-fallback` / cortes OpenAI.

## Implementações principais (Infrastructure)

- **Download:** `HttpVideoDownloader` (URL direta), `YoutubeExplodeVideoDownloader` (YouTube via YoutubeExplode)
- **Transcrição:** `OpenAiWhisperTranscriptionService` (API), `WhisperTranscriptionService` (local, Whisper.net)
- **Cortes:** `LlmEngagingMomentsService` (OpenAI), `OllamaEngagingMomentsService` (Ollama), `FallbackEngagingMomentsService` (Ollama → OpenAI)
- **Edição:** `FfmpegVideoEditor`
- **Pipeline:** `VideoClipPipeline(transcription, engagingMoments, editor, logger, downloader?, options?)`

## Licença

Uso interno / sob definição do repositório.
