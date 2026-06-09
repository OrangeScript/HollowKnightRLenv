import sys

from train_rl import main


if __name__ == "__main__":
    defaults = [
        "--boss-profile",
        "markoth",
        "--boss-scene",
        "GG_Markoth",
        "--n-steps",
        "2048",
        "--batch-size",
        "256",
        "--gamma",
        "0.997",
        "--ent-coef",
        "0.035",
        "--max-episode-steps",
        "6000",
        "--hero-death-penalty",
        "-120.0",
    ]
    main(defaults + sys.argv[1:])
