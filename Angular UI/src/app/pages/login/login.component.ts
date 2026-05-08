import { Component, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  username = '';
  password = '';
  error    = signal('');
  loading  = signal(false);

  constructor(private auth: AuthService, private router: Router) {}

  async onSubmit(): Promise<void> {
    if (!this.username || !this.password) {
      this.error.set('Please enter both username and password');
      return;
    }

    this.loading.set(true);
    this.error.set('');

    const success = await this.auth.login({ username: this.username, password: this.password });
    this.loading.set(false);

    if (success) {
      this.router.navigate(['/dashboard']);
    } else {
      this.error.set('Invalid credentials. Please check your username and password.');
    }
  }
}
