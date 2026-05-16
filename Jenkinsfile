pipeline {
    agent any

    environment {
        // Docker registry credentials (optional if using local registry)
        DOCKER_IMAGE_BACKEND = "smapi-api"
        DOCKER_IMAGE_FRONTEND = "sm-frontend"
    }

    stages {
        stage('Checkout') {
            steps {
                echo 'Checking out code from GitHub...'
                checkout scm
            }
        }

        stage('SonarQube Analysis') {
            steps {
                echo 'Skipping SonarQube for now to save RAM (2GB Limit)...'
                // sh 'sonar-scanner ...'
            }
        }

        stage('Build Docker Images') {
            steps {
                echo 'Building Docker images using Docker Compose...'
                sh 'docker-compose build'
            }
        }

        stage('Deploy') {
            steps {
                echo 'Deploying application using Docker Compose...'
                // -d means detached mode (run in background)
                // --remove-orphans cleans up old containers
                sh 'docker-compose up -d --remove-orphans'
            }
        }

        stage('Cleanup') {
            steps {
                echo 'Cleaning up unused Docker images...'
                sh 'docker image prune -f'
            }
        }
    }

    post {
        success {
            echo 'Deployment successful!'
        }
        failure {
            echo 'Deployment failed. Please check Jenkins logs.'
        }
    }
}
