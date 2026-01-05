use anyhow::{anyhow, Result};
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread;
use tracing::{debug, error, info, warn};
use wasapi::*;
use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};

use super::{AudioBuffer, AudioFormat};

pub struct AudioCapture {
    control_tx: Option<Sender<CaptureCommand>>,
    audio_rx: Option<Receiver<AudioBuffer>>,
    capture_thread: Option<thread::JoinHandle<()>>,
}

enum CaptureCommand {
    Stop,
}

impl AudioCapture {
    pub fn new() -> Self {
        Self {
            control_tx: None,
            audio_rx: None,
            capture_thread: None,
        }
    }

    pub fn start(&mut self, device_id: Option<String>) -> Result<()> {
        if self.capture_thread.is_some() {
            return Err(anyhow!("Capture already running"));
        }

        let (control_tx, control_rx) = mpsc::channel();
        let (audio_tx, audio_rx) = mpsc::channel();

        let handle = thread::spawn(move || {
            if let Err(e) = capture_loop(device_id, control_rx, audio_tx) {
                error!("Capture loop error: {}", e);
            }
        });

        self.control_tx = Some(control_tx);
        self.audio_rx = Some(audio_rx);
        self.capture_thread = Some(handle);

        info!("Audio capture started");
        Ok(())
    }

    pub fn stop(&mut self) -> Result<()> {
        if let Some(tx) = self.control_tx.take() {
            let _ = tx.send(CaptureCommand::Stop);
        }

        if let Some(handle) = self.capture_thread.take() {
            handle.join().map_err(|_| anyhow!("Thread join failed"))?;
        }

        self.audio_rx = None;
        info!("Audio capture stopped");
        Ok(())
    }

    pub fn try_recv(&self) -> Option<AudioBuffer> {
        self.audio_rx.as_ref()?.try_recv().ok()
    }

    pub fn recv(&self) -> Option<AudioBuffer> {
        self.audio_rx.as_ref()?.recv().ok()
    }
}

impl Drop for AudioCapture {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

fn capture_loop(
    _device_id: Option<String>,
    control_rx: Receiver<CaptureCommand>,
    audio_tx: Sender<AudioBuffer>,
) -> Result<()> {
    unsafe {
        CoInitializeEx(None, COINIT_MULTITHREADED).map_err(|e| anyhow!("COM init failed: {}", e))?;
    }

    let device = get_default_device(&Direction::Render)
        .map_err(|e| anyhow!("Failed to get default device: {}", e))?;

    let device_name = device
        .get_friendlyname()
        .map_err(|e| anyhow!("Failed to get device name: {}", e))?;
    info!("Capturing from: {}", device_name);

    let mut client = device
        .get_iaudioclient()
        .map_err(|e| anyhow!("Failed to get audio client: {}", e))?;

    let format = client
        .get_mixformat()
        .map_err(|e| anyhow!("Failed to get mix format: {}", e))?;
    debug!(
        "Mix format: {} Hz, {} ch, {} bits",
        format.get_samplespersec(),
        format.get_nchannels(),
        format.get_bitspersample()
    );

    let sharemode = ShareMode::Shared;
    let desired_format = WaveFormat::new(
        format.get_bitspersample() as usize,
        format.get_bitspersample() as usize,
        &SampleType::Int,
        format.get_samplespersec() as usize,
        format.get_nchannels() as usize,
        None,
    );

    client
        .initialize_client(
            &desired_format,
            0,
            &Direction::Capture,
            &sharemode,
            true,
        )
        .map_err(|e| anyhow!("Failed to initialize client: {}", e))?;

    let blockalign = desired_format.get_blockalign();
    let capture_client = client
        .get_audiocaptureclient()
        .map_err(|e| anyhow!("Failed to get capture client: {}", e))?;

    let h_event = client
        .set_get_eventhandle()
        .map_err(|e| anyhow!("Failed to get event handle: {}", e))?;

    client
        .start_stream()
        .map_err(|e| anyhow!("Failed to start stream: {}", e))?;
    info!("WASAPI capture stream started");

    let output_format = AudioFormat {
        sample_rate: format.get_samplespersec() as u32,
        channels: format.get_nchannels(),
        bits_per_sample: 16,
    };

    let mut data_buffer = vec![0u8; 48000 * 4 * 2];

    loop {
        if control_rx.try_recv().is_ok() {
            info!("Stop command received");
            break;
        }

        if h_event.wait_for_event(100).is_err() {
            continue;
        }

        match capture_client.read_from_device(blockalign as usize, &mut data_buffer) {
            Ok((frames, _flags)) if frames > 0 => {
                let bytes = frames as usize * blockalign as usize;
                let samples = convert_to_i16(&data_buffer[..bytes], format.get_bitspersample());
                let buffer = AudioBuffer::new(samples, output_format.clone());

                if audio_tx.send(buffer).is_err() {
                    warn!("Audio receiver dropped");
                    break;
                }
            }
            Ok(_) => {}
            Err(e) => {
                error!("Read error: {}", e);
            }
        }
    }

    let _ = client.stop_stream();
    info!("WASAPI capture stream stopped");

    Ok(())
}

fn convert_to_i16(data: &[u8], bits_per_sample: u16) -> Vec<i16> {
    match bits_per_sample {
        16 => data
            .chunks_exact(2)
            .map(|c| i16::from_le_bytes([c[0], c[1]]))
            .collect(),
        24 => data
            .chunks_exact(3)
            .map(|c| {
                let val = i32::from_le_bytes([0, c[0], c[1], c[2]]) >> 8;
                (val >> 8) as i16
            })
            .collect(),
        32 => data
            .chunks_exact(4)
            .map(|c| {
                let val = i32::from_le_bytes([c[0], c[1], c[2], c[3]]);
                (val >> 16) as i16
            })
            .collect(),
        _ => {
            warn!("Unsupported bit depth: {}", bits_per_sample);
            Vec::new()
        }
    }
}
