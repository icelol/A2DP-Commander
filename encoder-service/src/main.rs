mod audio;
mod encoder;
mod ipc;
mod output;

use anyhow::Result;
use tracing::{info, Level};
use tracing_subscriber::FmtSubscriber;

#[tokio::main]
async fn main() -> Result<()> {
    let subscriber = FmtSubscriber::builder()
        .with_max_level(Level::DEBUG)
        .finish();
    tracing::subscriber::set_global_default(subscriber)?;

    info!("A2DP Encoder Service starting...");

    let config = ipc::ServiceConfig::default();

    info!("Listening on pipe: {}", config.pipe_name);

    ipc::run_server(config).await?;

    Ok(())
}
