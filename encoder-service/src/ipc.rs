use anyhow::Result;
use interprocess::local_socket::{
    tokio::{prelude::*, Stream},
    GenericFilePath, ListenerOptions,
};
use serde::{Deserialize, Serialize};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tracing::{debug, error, info, warn};

use crate::audio::AudioCapture;
use crate::encoder::{AptxEncoder, AudioEncoder, Codec, LdacEncoder, LdacQuality, SbcEncoder};
use crate::output::{AudioOutput, OutputTarget, create_output};

#[derive(Debug, Clone)]
pub struct ServiceConfig {
    pub pipe_name: String,
}

impl Default for ServiceConfig {
    fn default() -> Self {
        Self {
            pipe_name: r"\\.\pipe\a2dp-encoder".to_string(),
        }
    }
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(tag = "type")]
pub enum Command {
    #[serde(rename = "start")]
    Start {
        codec: String,
        quality: Option<String>,
        sample_rate: Option<u32>,
        output: Option<String>,
        output_path: Option<String>,
    },
    #[serde(rename = "stop")]
    Stop,
    #[serde(rename = "status")]
    Status,
    #[serde(rename = "set_quality")]
    SetQuality { quality: String },
    #[serde(rename = "set_output")]
    SetOutput { output: String, path: Option<String> },
    #[serde(rename = "list_devices")]
    ListDevices,
    #[serde(rename = "ping")]
    Ping,
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(tag = "type")]
pub enum Response {
    #[serde(rename = "ok")]
    Ok { message: String },
    #[serde(rename = "error")]
    Error { message: String },
    #[serde(rename = "status")]
    Status {
        running: bool,
        codec: Option<String>,
        bitrate: Option<u32>,
        frames_encoded: u64,
        bytes_output: u64,
        output_target: String,
    },
    #[serde(rename = "devices")]
    Devices { devices: Vec<String> },
    #[serde(rename = "pong")]
    Pong,
}

struct EncoderState {
    capture: Option<AudioCapture>,
    encoder: Option<Box<dyn AudioEncoder>>,
    output: Option<Box<dyn AudioOutput>>,
    output_target: OutputTarget,
    frames_encoded: u64,
    bytes_output: u64,
}

impl EncoderState {
    fn new() -> Self {
        Self {
            capture: None,
            encoder: None,
            output: None,
            output_target: OutputTarget::Null,
            frames_encoded: 0,
            bytes_output: 0,
        }
    }

    fn is_running(&self) -> bool {
        self.capture.is_some()
    }

    fn start(&mut self, codec: Codec, quality: Option<LdacQuality>, output_target: OutputTarget) -> Result<()> {
        if self.is_running() {
            anyhow::bail!("Already running");
        }

        let sample_rate = 48000;
        let channels = 2;

        let encoder: Box<dyn AudioEncoder> = match codec {
            Codec::Ldac => Box::new(LdacEncoder::new(
                sample_rate,
                channels,
                quality.unwrap_or(LdacQuality::High),
            )?),
            Codec::Sbc => Box::new(SbcEncoder::new(sample_rate, channels)?),
            Codec::Aptx => Box::new(AptxEncoder::new(sample_rate, false)?),
            Codec::AptxHd => Box::new(AptxEncoder::new(sample_rate, true)?),
        };

        let output = create_output(&output_target)?;

        let mut capture = AudioCapture::new();
        capture.start(None)?;

        self.encoder = Some(encoder);
        self.capture = Some(capture);
        self.output = Some(output);
        self.output_target = output_target;
        self.frames_encoded = 0;
        self.bytes_output = 0;

        info!("Encoder started: {}", codec);
        Ok(())
    }

    fn stop(&mut self) -> Result<()> {
        if let Some(mut capture) = self.capture.take() {
            capture.stop()?;
        }
        if let Some(mut output) = self.output.take() {
            output.close()?;
        }
        self.encoder = None;
        info!("Encoder stopped, {} frames encoded, {} bytes output",
              self.frames_encoded, self.bytes_output);
        Ok(())
    }

    fn set_output(&mut self, target: OutputTarget) -> Result<()> {
        if let Some(mut old_output) = self.output.take() {
            old_output.close()?;
        }
        self.output = Some(create_output(&target)?);
        self.output_target = target;
        Ok(())
    }

    fn process_audio(&mut self) -> Result<()> {
        let capture = self.capture.as_ref().ok_or_else(|| anyhow::anyhow!("Not running"))?;
        let encoder = self.encoder.as_mut().ok_or_else(|| anyhow::anyhow!("No encoder"))?;
        let output = self.output.as_mut().ok_or_else(|| anyhow::anyhow!("No output"))?;

        while let Some(buffer) = capture.try_recv() {
            let encoded = encoder.encode(&buffer.data)?;
            if !encoded.is_empty() {
                output.write(&encoded)?;
                self.bytes_output += encoded.len() as u64;
            }
            self.frames_encoded += 1;
        }

        Ok(())
    }

    fn output_target_str(&self) -> String {
        match &self.output_target {
            OutputTarget::Null => "none".to_string(),
            OutputTarget::File { path } => format!("file:{}", path),
            OutputTarget::UsbAudio { device_name } => {
                device_name.as_ref().map_or("usb:default".to_string(), |n| format!("usb:{}", n))
            }
        }
    }
}

pub async fn run_server(config: ServiceConfig) -> Result<()> {
    let pipe_path = config.pipe_name.clone();

    // Create named pipe listener
    let listener = ListenerOptions::new()
        .name(pipe_path.as_str().to_fs_name::<GenericFilePath>()?)
        .create_tokio()?;

    info!("IPC server listening on: {}", pipe_path);

    let mut state = EncoderState::new();

    loop {
        match listener.accept().await {
            Ok(stream) => {
                info!("Client connected");
                if let Err(e) = handle_client(stream, &mut state).await {
                    error!("Client error: {}", e);
                }
            }
            Err(e) => {
                error!("Accept error: {}", e);
            }
        }
    }
}

async fn handle_client(stream: Stream, state: &mut EncoderState) -> Result<()> {
    let (reader, mut writer) = stream.split();
    let mut reader = BufReader::new(reader);
    let mut line = String::new();

    loop {
        line.clear();
        let bytes_read = reader.read_line(&mut line).await?;

        if bytes_read == 0 {
            debug!("Client disconnected");
            break;
        }

        let line = line.trim();
        if line.is_empty() {
            continue;
        }

        debug!("Received: {}", line);

        let response = match serde_json::from_str::<Command>(line) {
            Ok(cmd) => handle_command(cmd, state).await,
            Err(e) => Response::Error {
                message: format!("Invalid command: {}", e),
            },
        };

        let response_json = serde_json::to_string(&response)? + "\n";
        writer.write_all(response_json.as_bytes()).await?;
        writer.flush().await?;
    }

    Ok(())
}

fn parse_output_target(output: &Option<String>, path: &Option<String>) -> OutputTarget {
    match output.as_deref() {
        Some("file") => OutputTarget::File {
            path: path.clone().unwrap_or_else(|| "output.ldac".to_string()),
        },
        Some("usb") | Some("usb_audio") => OutputTarget::UsbAudio {
            device_name: path.clone(),
        },
        Some("null") | None => OutputTarget::Null,
        Some(other) => {
            warn!("Unknown output type '{}', using null", other);
            OutputTarget::Null
        }
    }
}

async fn handle_command(cmd: Command, state: &mut EncoderState) -> Response {
    match cmd {
        Command::Ping => Response::Pong,

        Command::Start {
            codec,
            quality,
            sample_rate: _,
            output,
            output_path,
        } => {
            let codec = match codec.to_lowercase().as_str() {
                "ldac" => Codec::Ldac,
                "sbc" => Codec::Sbc,
                "aptx" => Codec::Aptx,
                "aptxhd" | "aptx-hd" | "aptx_hd" => Codec::AptxHd,
                _ => {
                    return Response::Error {
                        message: format!("Unknown codec: {}", codec),
                    }
                }
            };

            let ldac_quality = quality.as_ref().and_then(|q| match q.to_lowercase().as_str() {
                "high" | "990" => Some(LdacQuality::High),
                "standard" | "660" => Some(LdacQuality::Standard),
                "mobile" | "330" => Some(LdacQuality::Mobile),
                _ => None,
            });

            let output_target = parse_output_target(&output, &output_path);

            match state.start(codec, ldac_quality, output_target) {
                Ok(()) => Response::Ok {
                    message: format!("Started {} encoder", codec),
                },
                Err(e) => Response::Error {
                    message: e.to_string(),
                },
            }
        }

        Command::Stop => match state.stop() {
            Ok(()) => Response::Ok {
                message: "Stopped".to_string(),
            },
            Err(e) => Response::Error {
                message: e.to_string(),
            },
        },

        Command::Status => {
            let (codec, bitrate) = if let Some(ref encoder) = state.encoder {
                (Some(encoder.codec().to_string()), Some(encoder.bitrate()))
            } else {
                (None, None)
            };

            Response::Status {
                running: state.is_running(),
                codec,
                bitrate,
                frames_encoded: state.frames_encoded,
                bytes_output: state.bytes_output,
                output_target: state.output_target_str(),
            }
        }

        Command::SetQuality { quality } => {
            warn!("SetQuality not yet implemented: {}", quality);
            Response::Error {
                message: "Not implemented".to_string(),
            }
        }

        Command::SetOutput { output, path } => {
            let target = parse_output_target(&Some(output), &path);
            match state.set_output(target) {
                Ok(()) => Response::Ok {
                    message: format!("Output set to {}", state.output_target_str()),
                },
                Err(e) => Response::Error {
                    message: e.to_string(),
                },
            }
        }

        Command::ListDevices => {
            match crate::output::UsbAudioOutput::list_devices() {
                Ok(devices) => Response::Devices { devices },
                Err(e) => Response::Error {
                    message: format!("Failed to list devices: {}", e),
                },
            }
        }
    }
}
