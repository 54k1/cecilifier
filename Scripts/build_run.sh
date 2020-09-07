#!/bin/bash


#sudo docker run cecilifier:net5 --rm -p 9090:8081

sudo docker build -t cecilifier:net5 .
sudo docker run --rm -p 9090:8081 cecilifier:net5
