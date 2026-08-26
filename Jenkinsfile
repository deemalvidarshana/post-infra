pipeline {
    agent any

    environment {
        // Docker registry credentials (optional if using local registry)
        DOCKER_IMAGE_BACKEND = "smapi-api"
        DOCKER_IMAGE_FRONTEND = "sm-frontend"
        COMPOSE_PROJECT_NAME = "sm-automate"
        DOWNLOADS_HOST_PATH = "/opt/sm-automate/downloads"
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
                sh 'docker-compose -p "$COMPOSE_PROJECT_NAME" build'
            }
        }

        stage('Deploy') {
            steps {
                echo 'Deploying application using Docker Compose...'
                // -d means detached mode (run in background)
                // --remove-orphans cleans up old containers
                sh 'docker-compose -p "$COMPOSE_PROJECT_NAME" up -d --remove-orphans'
            }
        }

        stage('Verify HTTPS Proxy') {
            steps {
                echo 'Validating the Caddy reverse proxy...'
                sh 'docker-compose -p "$COMPOSE_PROJECT_NAME" ps'
                sh 'docker-compose -p "$COMPOSE_PROJECT_NAME" exec -T caddy caddy validate --config /etc/caddy/Caddyfile'
                sh 'docker-compose -p "$COMPOSE_PROJECT_NAME" logs --no-color --tail=100 caddy'
                sh 'test "$(docker inspect --format="{{.State.Running}}" sm-caddy)" = "true"'
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
