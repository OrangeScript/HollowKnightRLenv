import sys

from train_rl import main


if __name__ == "__main__":
    defaults = [
        "--boss-profile",
        "hornet",
        "--boss-scene",
        "GG_Hornet_2",
        "--n-steps",
        "1024",
        "--batch-size",
        "256",
        "--gamma",
        "0.995",
        "--ent-coef",
        "0.02",
        "--max-episode-steps",
        "3600",
    ]
    main(defaults + sys.argv[1:])
