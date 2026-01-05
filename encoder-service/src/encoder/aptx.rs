use anyhow::{anyhow, Result};
use tracing::{debug, info};

use super::aptx_ffi::{aptx_encode, aptx_encode_finish, aptx_finish, aptx_init, AptxContext};
use super::{AudioEncoder, Codec};

pub struct AptxEncoder {
    ctx: *mut AptxContext,
    hd: bool,
    sample_rate: u32,
    input_buffer: Vec<u8>,
}

impl AptxEncoder {
    pub fn new(sample_rate: u32, hd: bool) -> Result<Self> {
        let ctx = unsafe { aptx_init(if hd { 1 } else { 0 }) };
        if ctx.is_null() {
            return Err(anyhow!("Failed to initialize aptX context"));
        }

        let codec_name = if hd { "aptX HD" } else { "aptX" };
        info!(
            "{} encoder: {} Hz, bitrate ~{} kbps",
            codec_name,
            sample_rate,
            if hd { 576 } else { 352 }
        );

        Ok(Self {
            ctx,
            hd,
            sample_rate,
            input_buffer: Vec::with_capacity(24 * 16),
        })
    }

    fn convert_samples_to_24bit(&self, pcm: &[i16]) -> Vec<u8> {
        let mut output = Vec::with_capacity(pcm.len() * 3);

        for chunk in pcm.chunks(2) {
            if chunk.len() < 2 {
                break;
            }

            let left = (chunk[0] as i32) << 8;
            let right = (chunk[1] as i32) << 8;

            output.push((left & 0xFF) as u8);
            output.push(((left >> 8) & 0xFF) as u8);
            output.push(((left >> 16) & 0xFF) as u8);

            output.push((right & 0xFF) as u8);
            output.push(((right >> 8) & 0xFF) as u8);
            output.push(((right >> 16) & 0xFF) as u8);
        }

        output
    }
}

impl Drop for AptxEncoder {
    fn drop(&mut self) {
        if !self.ctx.is_null() {
            unsafe { aptx_finish(self.ctx) };
            debug!("aptX encoder destroyed");
        }
    }
}

impl AudioEncoder for AptxEncoder {
    fn codec(&self) -> Codec {
        if self.hd {
            Codec::AptxHd
        } else {
            Codec::Aptx
        }
    }

    fn encode(&mut self, pcm: &[i16]) -> Result<Vec<u8>> {
        let input_24bit = self.convert_samples_to_24bit(pcm);
        self.input_buffer.extend_from_slice(&input_24bit);

        let output_frame_size = if self.hd { 6 } else { 4 };
        let max_output_size = (self.input_buffer.len() / 24) * output_frame_size + 64;
        let mut output = vec![0u8; max_output_size];
        let mut total_written = 0usize;

        while self.input_buffer.len() >= 24 {
            let mut written = 0usize;
            let processed = unsafe {
                aptx_encode(
                    self.ctx,
                    self.input_buffer.as_ptr(),
                    self.input_buffer.len(),
                    output.as_mut_ptr().add(total_written),
                    output.len() - total_written,
                    &mut written,
                )
            };

            if processed == 0 {
                break;
            }

            self.input_buffer.drain(..processed);
            total_written += written;
        }

        output.truncate(total_written);
        Ok(output)
    }

    fn frame_size(&self) -> usize {
        8
    }

    fn bitrate(&self) -> u32 {
        if self.hd {
            576_000
        } else {
            352_000
        }
    }
}

unsafe impl Send for AptxEncoder {}
