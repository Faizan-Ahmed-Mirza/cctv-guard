import { Component, signal, computed, OnInit, OnDestroy, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, AiSettings } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { CameraStatusService } from '../../services/camera-status.service';
import { Camera, ManagedUser } from '../../models';
import { firstValueFrom, Subscription } from 'rxjs';

@Component({
  selector: 'app-configuration',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './configuration.component.html',
  styleUrl: './configuration.component.scss'
})
export class ConfigurationComponent implements OnInit, OnDestroy {
  activeTab   = signal<'cameras' | 'ai' | 'users' | 'faces' | 'system'>('cameras');
  saveSuccess = signal('');
  saveError   = signal('');
  loading     = signal(true);

  // ── Cameras ───────────────────────────────────────────────────────────────
  cameras               = signal<Camera[]>([]);
  cameraModalOpen       = signal(false);
  cameraModalMode       = signal<'add' | 'edit'>('add');
  editingId             = signal<string | null>(null);
  deleteCameraConfirmId = signal<string | null>(null);
  cameraForm: Partial<Camera> = this.blankCameraForm();

  cameraModalTitle       = computed(() => this.cameraModalMode() === 'add' ? 'Add New Camera' : 'Edit Camera');
  cameraModalSubmitLabel = computed(() => this.cameraModalMode() === 'add' ? 'Add Camera' : 'Save Changes');

  // ── AI Settings ───────────────────────────────────────────────────────────
  aiSettings = signal<AiSettings>({
    fightDetection: true, weaponDetection: true, intrusionDetection: true,
    faceRecognition: true, licensePlate: true, globalConfidence: 0.85,
    alertLatencyTarget: 2, frameProcessingRate: 30, gpuAcceleration: true, modelVersion: 'YOLOv8n'
  });

  // ── Users ─────────────────────────────────────────────────────────────────
  managedUsers    = signal<ManagedUser[]>([]);
  userFilter      = signal<'all' | 'Admin' | 'Operator' | 'Viewer'>('all');
  userSearch      = signal('');
  userModalOpen   = signal(false);
  userModalMode   = signal<'add' | 'edit'>('add');
  editingUserId   = signal<string | null>(null);
  deleteConfirmId = signal<string | null>(null);
  userForm: Partial<ManagedUser> & { password?: string; confirmPassword?: string } = this.blankUserForm();

  filteredUsers = computed(() => {
    let list = this.managedUsers();
    const role = this.userFilter();
    const q    = this.userSearch().toLowerCase().trim();
    if (role !== 'all') list = list.filter(u => u.role === role);
    if (q) list = list.filter(u =>
      u.username.toLowerCase().includes(q) || u.email.toLowerCase().includes(q)
    );
    return list;
  });

  userStats = computed(() => {
    const all = this.managedUsers();
    return {
      total:     all.length,
      admins:    all.filter(u => u.role === 'Admin').length,
      operators: all.filter(u => u.role === 'Operator').length,
      viewers:   all.filter(u => u.role === 'Viewer').length,
      active:    all.filter(u => u.status === 'active').length,
      suspended: all.filter(u => u.status === 'suspended').length,
    };
  });

  userModalTitle = computed(() => this.userModalMode() === 'add' ? 'Add New User' : 'Edit User');

  private statusSub?: Subscription;

  constructor(private api: ApiService, public auth: AuthService, private cameraStatus: CameraStatusService) {}

  async ngOnInit(): Promise<void> {
    try {
      this.cameras.set(await firstValueFrom(this.api.getCameras()));
      if (this.auth.isAdmin()) {
        const [users, ai] = await Promise.all([
          firstValueFrom(this.api.getUsers()),
          firstValueFrom(this.api.getAiSettings()),
        ]);
        this.managedUsers.set(users);
        this.aiSettings.set(ai);
        // Load registered faces for the face recognition tab
        await this.loadFaces();
      }
    } finally {
      this.loading.set(false);
    }

    // Listen for real-time camera status changes from the backend health checker
    await this.cameraStatus.connect();
    this.statusSub = this.cameraStatus.statusChange$.subscribe(event => {
      this.cameras.update(list =>
        list.map(c => c.id === event.id ? { ...c, status: event.status } : c)
      );
    });
  }

  ngOnDestroy(): void {
    this.statusSub?.unsubscribe();
    this.stopWebcamStream();
  }

  // ── Camera methods ────────────────────────────────────────────────────────
  openAddModal(): void {
    if (!this.auth.isAdmin()) return;
    this.cameraForm = this.blankCameraForm();
    this.editingId.set(null);
    this.cameraModalMode.set('add');
    this.cameraModalOpen.set(true);
  }

  openEditModal(cam: Camera): void {
    if (!this.auth.isAdmin()) return;
    this.cameraForm = { ...cam };
    this.editingId.set(cam.id);
    this.cameraModalMode.set('edit');
    this.cameraModalOpen.set(true);
  }

  closeModal(): void { this.cameraModalOpen.set(false); }

  async submitCameraForm(): Promise<void> {
    if (!this.auth.isAdmin()) return;
    if (!this.cameraForm.name?.trim() || !this.cameraForm.ipAddress?.trim()) return;
    try {
      if (this.cameraModalMode() === 'add') {
        const cam = await firstValueFrom(this.api.createCamera(this.cameraForm));
        this.cameras.update(list => [...list, cam]);
        this.showSuccess('Camera added successfully');
      } else {
        const id = this.editingId()!;
        const cam = await firstValueFrom(this.api.updateCamera(id, this.cameraForm));
        this.cameras.update(list => list.map(c => c.id === id ? cam : c));
        this.showSuccess('Camera updated');
      }
      this.cameraModalOpen.set(false);
    } catch { this.showError('Failed to save camera.'); }
  }

  deleteCamera(id: string): void {
    if (!this.auth.isAdmin()) return;
    this.deleteCameraConfirmId.set(id);
  }

  cancelDeleteCamera(): void { this.deleteCameraConfirmId.set(null); }

  async executeDeleteCamera(): Promise<void> {
    if (!this.auth.isAdmin()) return;
    const id = this.deleteCameraConfirmId();
    if (!id) return;
    try {
      await firstValueFrom(this.api.deleteCamera(id));
      this.cameras.update(list => list.filter(c => c.id !== id));
      this.deleteCameraConfirmId.set(null);
      this.showSuccess('Camera deleted successfully');
    } catch { this.showError('Failed to delete camera.'); }
  }

  getCameraDeleteTarget(): Camera | undefined {
    return this.cameras().find(c => c.id === this.deleteCameraConfirmId());
  }

  async toggleDetection(id: string, enabled: boolean): Promise<void> {
    try {
      const cam = await firstValueFrom(this.api.patchCameraDetection(id, enabled));
      this.cameras.update(list => list.map(c => c.id === id ? cam : c));
    } catch { this.showError('Failed to update detection setting.'); }
  }

  // ── AI methods ────────────────────────────────────────────────────────────
  async toggleModule(key: string, value: boolean): Promise<void> {
    if (!this.auth.isAdmin()) return;
    // Update local signal immediately for instant UI feedback
    this.aiSettings.update(s => ({ ...s, [key]: value }));
    // Auto-save to DB so the backend picks it up within 30s
    try {
      await firstValueFrom(this.api.updateAiSettings(this.aiSettings()));
      this.showSuccess(`${key.replace(/([A-Z])/g, ' $1').trim()} ${value ? 'enabled' : 'disabled'}`);
    } catch {
      // Revert on failure
      this.aiSettings.update(s => ({ ...s, [key]: !value }));
      this.showError('Failed to save setting.');
    }
  }

  async saveAiSettings(): Promise<void> {
    if (!this.auth.isAdmin()) return;
    try {
      const updated = await firstValueFrom(this.api.updateAiSettings(this.aiSettings()));
      this.aiSettings.set(updated);
      this.showSuccess('AI settings saved successfully');
    } catch { this.showError('Failed to save AI settings.'); }
  }

  // ── User methods ──────────────────────────────────────────────────────────
  openAddUserModal(): void {
    this.userForm = this.blankUserForm();
    this.editingUserId.set(null);
    this.userModalMode.set('add');
    this.userModalOpen.set(true);
    this.saveError.set('');
  }

  openEditUserModal(user: ManagedUser): void {
    this.userForm = { ...user, password: '', confirmPassword: '' };
    this.editingUserId.set(user.id);
    this.userModalMode.set('edit');
    this.userModalOpen.set(true);
    this.saveError.set('');
  }

  closeUserModal(): void { this.userModalOpen.set(false); this.saveError.set(''); }

  async submitUserForm(): Promise<void> {
    const { username, email, role, password, confirmPassword } = this.userForm;
    if (!username?.trim() || !email?.trim() || !role) { this.saveError.set('Username, email and role are required.'); return; }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))   { this.saveError.set('Please enter a valid email address.'); return; }
    try {
      if (this.userModalMode() === 'add') {
        if (!password || password.length < 8) { this.saveError.set('Password must be at least 8 characters.'); return; }
        if (password !== confirmPassword)      { this.saveError.set('Passwords do not match.'); return; }
        const user = await firstValueFrom(this.api.createUser({ username, email, role, password, status: this.userForm.status ?? 'active' }));
        this.managedUsers.update(list => [...list, user]);
        this.showSuccess(`User "${username}" added successfully`);
      } else {
        const id = this.editingUserId()!;
        if (password && password.length > 0) {
          if (password.length < 8)         { this.saveError.set('Password must be at least 8 characters.'); return; }
          if (password !== confirmPassword) { this.saveError.set('Passwords do not match.'); return; }
        }
        const user = await firstValueFrom(this.api.updateUser(id, { username, email, role, status: this.userForm.status, password: password || undefined }));
        this.managedUsers.update(list => list.map(u => u.id === id ? user : u));
        this.showSuccess(`User "${username}" updated successfully`);
      }
      this.userModalOpen.set(false);
      this.saveError.set('');
    } catch (err: any) {
      this.saveError.set(err?.error?.message ?? 'Failed to save user.');
    }
  }

  confirmDeleteUser(id: string): void { this.deleteConfirmId.set(id); }
  cancelDeleteUser(): void            { this.deleteConfirmId.set(null); }

  async executeDeleteUser(): Promise<void> {
    const id = this.deleteConfirmId();
    if (!id) return;
    try {
      await firstValueFrom(this.api.deleteUser(id));
      this.managedUsers.update(list => list.filter(u => u.id !== id));
      this.deleteConfirmId.set(null);
      this.showSuccess('User removed successfully');
    } catch (err: any) {
      this.showError(err?.error?.message ?? 'Failed to delete user.');
      this.deleteConfirmId.set(null);
    }
  }

  async toggleUserStatus(user: ManagedUser): Promise<void> {
    const newStatus = user.status === 'active' ? 'suspended' : 'active';
    try {
      const updated = await firstValueFrom(this.api.patchUserStatus(user.id, newStatus));
      this.managedUsers.update(list => list.map(u => u.id === user.id ? updated : u));
      this.showSuccess(`User ${newStatus === 'active' ? 'activated' : 'suspended'} successfully`);
    } catch (err: any) { this.showError(err?.error?.message ?? 'Failed to update status.'); }
  }

  async changeUserRole(user: ManagedUser, newRole: 'Admin' | 'Operator' | 'Viewer'): Promise<void> {
    try {
      const updated = await firstValueFrom(this.api.patchUserRole(user.id, newRole));
      this.managedUsers.update(list => list.map(u => u.id === user.id ? updated : u));
      this.showSuccess(`Role updated to ${newRole}`);
    } catch (err: any) { this.showError(err?.error?.message ?? 'Failed to update role.'); }
  }

  getUserDeleteTarget(): ManagedUser | undefined {
    return this.managedUsers().find(u => u.id === this.deleteConfirmId());
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  private blankCameraForm(): Partial<Camera> {
    return { name: '', location: '', ipAddress: '', port: 554, detectionEnabled: true, confidenceThreshold: 0.85, frameRate: 30, status: 'offline', rtspUrl: '' };
  }

  private blankUserForm(): Partial<ManagedUser> & { password?: string; confirmPassword?: string } {
    return { username: '', email: '', role: 'Operator', status: 'active', password: '', confirmPassword: '' };
  }

  private showSuccess(msg: string): void {
    this.saveSuccess.set(msg); this.saveError.set('');
    setTimeout(() => this.saveSuccess.set(''), 3500);
  }

  private showError(msg: string): void {
    this.saveError.set(msg);
    setTimeout(() => this.saveError.set(''), 4000);
  }

  getStatusClass(s: string): string {
    return ({ online: 'success', offline: 'secondary', error: 'danger' } as Record<string,string>)[s] ?? 'secondary';
  }

  // ── Stream URL helpers ────────────────────────────────────────────────────
  getProtocol(url?: string): 'rtsp' | 'rtmp' {
    return url?.toLowerCase().startsWith('rtmp') ? 'rtmp' : 'rtsp';
  }

  getStreamPlaceholder(url?: string): string {
    return this.getProtocol(url) === 'rtmp'
      ? 'rtmp://[user:pass@]192.168.1.x:1935[/path]'
      : 'rtsp://[user:pass@]192.168.1.x:554/stream';
  }

  onProtocolChange(protocol: 'rtsp' | 'rtmp'): void {
    const current = this.cameraForm.rtspUrl ?? '';
    // Swap the protocol prefix, keep the rest of the URL
    if (protocol === 'rtmp' && current.startsWith('rtsp://')) {
      this.cameraForm = { ...this.cameraForm, rtspUrl: current.replace('rtsp://', 'rtmp://') };
    } else if (protocol === 'rtsp' && current.startsWith('rtmp://')) {
      this.cameraForm = { ...this.cameraForm, rtspUrl: current.replace('rtmp://', 'rtsp://') };
    } else if (!current) {
      // Empty — set a starter prefix
      this.cameraForm = { ...this.cameraForm, rtspUrl: protocol === 'rtmp' ? 'rtmp://' : 'rtsp://' };
    }
  }
  getRoleClass(role: string): string {
    return ({ Admin: 'danger', Operator: 'warning', Viewer: 'info' } as Record<string,string>)[role] ?? 'secondary';
  }
  getRoleIcon(role: string): string {
    return ({ Admin: '👑', Operator: '🛡️', Viewer: '👁️' } as Record<string,string>)[role] ?? '👤';
  }
  formatDate(d: Date | null): string {
    if (!d) return 'Never';
    const diff = Date.now() - new Date(d).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1)  return 'Just now';
    if (mins < 60) return `${mins}m ago`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24)  return `${hrs}h ago`;
    const days = Math.floor(hrs / 24);
    if (days < 7)  return `${days}d ago`;
    return new Date(d).toLocaleDateString();
  }

  // ── Face Recognition methods ──────────────────────────────────────────────
  registeredFaces  = signal<string[]>([]);
  faceLoading      = signal(false);
  faceRegUsername  = signal('');
  faceRegFile      = signal<File | null>(null);
  faceRegPreview   = signal<string | null>(null);
  faceRegError     = signal('');
  faceDeleteTarget = signal<string | null>(null);
  faceInputMode    = signal<'upload' | 'webcam'>('upload');
  webcamCaptured   = signal(false);

  // Store video/canvas element refs set from template
  private webcamVideoEl: HTMLVideoElement | null = null;
  private webcamCanvasEl: HTMLCanvasElement | null = null;
  private webcamStream: MediaStream | null = null;

  /** Called from template (click)="startWebcam()" */
  async startWebcam(): Promise<void> {
    this.faceInputMode.set('webcam');
    this.webcamCaptured.set(false);
    this.faceRegFile.set(null);
    this.faceRegPreview.set(null);
    this.faceRegError.set('');
    // Wait two ticks for Angular to render the video element
    await new Promise(r => setTimeout(r, 150));
    try {
      this.webcamVideoEl  = document.getElementById('webcamVideo')  as HTMLVideoElement;
      this.webcamCanvasEl = document.getElementById('webcamCanvas') as HTMLCanvasElement;
      if (!this.webcamVideoEl) {
        this.faceRegError.set('Webcam element not found. Try again.');
        return;
      }
      this.webcamStream = await navigator.mediaDevices.getUserMedia({
        video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' }
      });
      this.webcamVideoEl.srcObject = this.webcamStream;
      await this.webcamVideoEl.play();
    } catch (err: any) {
      const msg = err?.name === 'NotAllowedError'
        ? 'Camera permission denied. Allow camera access in browser settings.'
        : 'Could not access webcam. Make sure no other app is using it.';
      this.faceRegError.set(msg);
      this.faceInputMode.set('upload');
    }
  }

  captureWebcam(): void {
    const video  = this.webcamVideoEl;
    const canvas = this.webcamCanvasEl ?? document.getElementById('webcamCanvas') as HTMLCanvasElement;
    if (!video || !canvas) {
      this.faceRegError.set('Webcam not ready. Please try again.');
      return;
    }

    const w = video.videoWidth  || 640;
    const h = video.videoHeight || 480;
    canvas.width  = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.drawImage(video, 0, 0, w, h);

    canvas.toBlob(blob => {
      if (!blob) { this.faceRegError.set('Capture failed. Try again.'); return; }
      const file = new File([blob], 'webcam-capture.jpg', { type: 'image/jpeg' });
      this.faceRegFile.set(file);
      this.faceRegPreview.set(canvas.toDataURL('image/jpeg', 0.92));
      this.webcamCaptured.set(true);
      this.stopWebcamStream();
    }, 'image/jpeg', 0.92);
  }

  retakeWebcam(): void {
    this.webcamCaptured.set(false);
    this.faceRegFile.set(null);
    this.faceRegPreview.set(null);
    this.startWebcam();
  }

  private stopWebcamStream(): void {
    if (this.webcamStream) {
      this.webcamStream.getTracks().forEach(t => t.stop());
      this.webcamStream = null;
    }
    if (this.webcamVideoEl) {
      this.webcamVideoEl.srcObject = null;
      this.webcamVideoEl = null;
    }
  }

  async loadFaces(): Promise<void> {
    this.faceLoading.set(true);
    this.faceRegError.set('');
    try {
      const res = await firstValueFrom(this.api.getFaces());
      this.registeredFaces.set(res.faces);
    } catch {
      this.faceRegError.set('Could not load faces — is the AI service running?');
    } finally {
      this.faceLoading.set(false);
    }
  }

  onFaceFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0] ?? null;
    this.faceRegFile.set(file);
    if (file) {
      const reader = new FileReader();
      reader.onload = e => this.faceRegPreview.set(e.target?.result as string);
      reader.readAsDataURL(file);
    } else {
      this.faceRegPreview.set(null);
    }
  }

  async registerFace(): Promise<void> {
    const username = this.faceRegUsername().trim();
    const file     = this.faceRegFile();
    if (!username) { this.faceRegError.set('Enter a username.'); return; }
    if (!file)     { this.faceRegError.set('Select a photo.'); return; }

    this.faceLoading.set(true);
    this.faceRegError.set('');
    try {
      await firstValueFrom(this.api.registerFace(username, file));
      this.showSuccess(`Face registered for "${username}"`);
      this.faceRegUsername.set('');
      this.faceRegFile.set(null);
      this.faceRegPreview.set(null);
      this.webcamCaptured.set(false);
      this.faceInputMode.set('upload');
      await this.loadFaces();
    } catch (err: any) {
      const msg = err?.error?.detail
                ?? err?.error?.message
                ?? err?.message
                ?? 'Registration failed. Make sure the AI service is running.';
      this.faceRegError.set(msg);
    } finally {
      this.faceLoading.set(false);
    }
  }

  confirmDeleteFace(username: string): void { this.faceDeleteTarget.set(username); }
  cancelDeleteFace(): void                  { this.faceDeleteTarget.set(null); }

  async executeDeleteFace(): Promise<void> {
    const username = this.faceDeleteTarget();
    if (!username) return;
    try {
      await firstValueFrom(this.api.deleteFace(username));
      this.registeredFaces.update(list => list.filter(f => f !== username));
      this.faceDeleteTarget.set(null);
      this.showSuccess(`Face removed for "${username}"`);
    } catch {
      this.showError('Failed to remove face.');
      this.faceDeleteTarget.set(null);
    }
  }
}
