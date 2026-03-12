use sqlx::postgres::PgPoolOptions;
use dotenvy::dotenv;
use std::{env};
use crate::analyze::{analyze_and_store, MatchData};

pub mod analyze;
#[tokio::main]
async fn main() -> Result<(), sqlx::Error> {

    let file_path = std::env::args().nth(1).expect("Please provide a demo directory as an arg");
    println!("Provided directory: {}", file_path);
    dotenv().ok().expect("Failed to load .env file");

    let conn_pool = PgPoolOptions::new()
        .max_connections(5)
        .connect(&env::var("DATABASE_URL").expect("DATABASE_URL must be set"))
        .await?;
    let paths = std::fs::read_dir(&file_path).expect("Failed to read the provided directory");
    for path in paths {
        let path = path.expect("Failed to read a file in the directory").path();
        if path.extension().and_then(|s| s.to_str()) == Some("json") && !path.file_name().unwrap_or_default().to_string_lossy().contains("Stats.json")
        {
            let file_content = std::fs::read_to_string(&path).expect("Failed to read the JSON file");
            let match_data: MatchData = serde_json::from_str(&file_content).expect("Failed to deserialize JSON into MatchData struct");
            println!("Parsed MatchData: {}", match_data.header.map);
            let result_type = analyze_and_store(conn_pool.clone(), path.to_string_lossy().to_string(), match_data).await;
            match result_type {
                Ok(_) => println!("Successfully analyzed and stored data for file: {}", path.display()),
                Err(e) => eprintln!("Error analyzing and storing data for file {}: {}", path
                    .display(), e),
            }
        }
    }

    Ok(())
}
