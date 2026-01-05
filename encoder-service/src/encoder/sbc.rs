use anyhow::Result;
use tracing::{debug, info};

use super::{AudioEncoder, Codec};

const SBC_FRAME_SAMPLES: usize = 128;

pub struct SbcEncoder {
    sample_rate: u32,
    channels: u16,
    bitpool: u8,
    frame_buffer: Vec<i16>,
    initialized: bool,
}

impl SbcEncoder {
    pub fn new(sample_rate: u32, channels: u16) -> Result<Self> {
        let bitpool = 53; // High quality SBC

        info!(
            "SBC encoder: {} Hz, {} ch, bitpool {}",
            sample_rate, channels, bitpool
        );

        Ok(Self {
            sample_rate,
            channels,
            bitpool,
            frame_buffer: Vec::with_capacity(SBC_FRAME_SAMPLES * channels as usize * 2),
            initialized: false,
        })
    }

    fn init_if_needed(&mut self) -> Result<()> {
        if self.initialized {
            return Ok(());
        }

        debug!("SBC encoder initialized (stub)");
        self.initialized = true;
        Ok(())
    }

    fn encode_frame(&mut self, pcm: &[i16]) -> Result<Vec<u8>> {
        // SBC frame is typically ~100-120 bytes at high quality
        let frame_bytes = 116;

        let mut encoded = vec![0u8; frame_bytes];

        // SBC sync word
        encoded[0] = 0x9C;

        // Simple checksum
        let checksum: u32 = pcm.iter().map(|&s| s.abs() as u32).sum();
        encoded[1..5].copy_from_slice(&checksum.to_le_bytes());

        Ok(encoded)
    }
}

impl AudioEncoder for SbcEncoder {
    fn codec(&self) -> Codec {
        Codec::Sbc
    }

    fn encode(&mut self, pcm: &[i16]) -> Result<Vec<u8>> {
        self.init_if_needed()?;

        self.frame_buffer.extend_from_slice(pcm);

        let frame_samples = SBC_FRAME_SAMPLES * self.channels as usize;
        let mut output = Vec::new();

        while self.frame_buffer.len() >= frame_samples {
            let frame: Vec<i16> = self.frame_buffer.drain(..frame_samples).collect();
            let encoded = self.encode_frame(&frame)?;
            output.extend(encoded);
        }

        Ok(output)
    }

    fn frame_size(&self) -> usize {
        SBC_FRAME_SAMPLES * self.channels as usize
    }

    fn bitrate(&self) -> u32 {
        // SBC at bitpool 53, 48kHz stereo ≈ 328 kbps
        328000
    }
}
