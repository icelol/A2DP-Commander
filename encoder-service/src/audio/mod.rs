mod capture;
mod resampler;

pub use capture::AudioCapture;
pub use resampler::Resampler;

#[derive(Debug, Clone)]
pub struct AudioFormat {
    pub sample_rate: u32,
    pub channels: u16,
    pub bits_per_sample: u16,
}

impl Default for AudioFormat {
    fn default() -> Self {
        Self {
            sample_rate: 48000,
            channels: 2,
            bits_per_sample: 16,
        }
    }
}

#[derive(Debug, Clone)]
pub struct AudioBuffer {
    pub data: Vec<i16>,
    pub format: AudioFormat,
}

impl AudioBuffer {
    pub fn new(data: Vec<i16>, format: AudioFormat) -> Self {
        Self { data, format }
    }

    pub fn samples_per_channel(&self) -> usize {
        self.data.len() / self.format.channels as usize
    }

    pub fn duration_ms(&self) -> f64 {
        (self.samples_per_channel() as f64 / self.format.sample_rate as f64) * 1000.0
    }
}
