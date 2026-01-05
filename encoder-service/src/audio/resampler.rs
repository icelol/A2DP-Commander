use anyhow::Result;
use rubato::{FftFixedIn, Resampler as RubatoResampler};
use tracing::debug;

pub struct Resampler {
    resampler: Option<FftFixedIn<f32>>,
    input_rate: u32,
    output_rate: u32,
    channels: usize,
}

impl Resampler {
    pub fn new(input_rate: u32, output_rate: u32, channels: usize) -> Result<Self> {
        let resampler = if input_rate != output_rate {
            debug!(
                "Creating resampler: {} Hz -> {} Hz, {} ch",
                input_rate, output_rate, channels
            );

            let chunk_size = 1024;
            Some(FftFixedIn::new(
                input_rate as usize,
                output_rate as usize,
                chunk_size,
                2,
                channels,
            )?)
        } else {
            None
        };

        Ok(Self {
            resampler,
            input_rate,
            output_rate,
            channels,
        })
    }

    pub fn process(&mut self, input: &[i16]) -> Result<Vec<i16>> {
        if self.resampler.is_none() {
            return Ok(input.to_vec());
        }

        let resampler = self.resampler.as_mut().unwrap();
        let samples_per_channel = input.len() / self.channels;

        let mut channel_data: Vec<Vec<f32>> = vec![Vec::with_capacity(samples_per_channel); self.channels];

        for (i, sample) in input.iter().enumerate() {
            let ch = i % self.channels;
            channel_data[ch].push(*sample as f32 / 32768.0);
        }

        let output_frames = resampler.output_frames_max();
        let mut output_channels: Vec<Vec<f32>> = vec![vec![0.0; output_frames]; self.channels];

        let (_, out_len) = resampler.process_into_buffer(&channel_data, &mut output_channels, None)?;

        let mut output = Vec::with_capacity(out_len * self.channels);
        for i in 0..out_len {
            for ch in 0..self.channels {
                let sample = (output_channels[ch][i] * 32767.0).clamp(-32768.0, 32767.0) as i16;
                output.push(sample);
            }
        }

        Ok(output)
    }

    pub fn input_rate(&self) -> u32 {
        self.input_rate
    }

    pub fn output_rate(&self) -> u32 {
        self.output_rate
    }
}
